using System.Reflection;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using ScoramAPI.Controllers;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Hubs;
using ScoramAPI.Models;
using ScoramAPI.Services;
using Xunit;

namespace ScoramAPI.Tests.Controllers
{
    // In-memory stand-in for the private Azure container: tracks what exists, what was deleted and how
    // often, and can be told to fail -- so deletion tests can prove "idempotent" and "retry after failure"
    // rather than just "a method was called". No Azure credentials are involved anywhere.
    internal sealed class FakeBlobStore : IAzureBlobService
    {
        public Dictionary<string, byte[]> Blobs { get; } = new();
        public List<string> DownloadCalls { get; } = new();
        public List<string> DeleteCalls { get; } = new();
        public bool FailDeletes { get; set; }
        public HashSet<string> FailDeletesFor { get; } = new(); // fail only these blobs (others still delete)
        public bool FailDownloads { get; set; }

        public FakeBlobStore With(string blobName, string content = "private-bytes")
        {
            Blobs[blobName] = Encoding.UTF8.GetBytes(content);
            return this;
        }

        public string BuildBlobName(DocumentCategory category, string fileExtension) => throw new NotSupportedException();

        public Task UploadAsync(string blobName, Stream content, string contentType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream?> DownloadAsync(string blobName, CancellationToken cancellationToken = default)
        {
            DownloadCalls.Add(blobName);
            if (FailDownloads) throw new InvalidOperationException("storage down: AccountKey=super-secret");
            return Task.FromResult<Stream?>(Blobs.TryGetValue(blobName, out var bytes) ? new MemoryStream(bytes) : null);
        }

        public Task<bool> ExistsAsync(string blobName, CancellationToken cancellationToken = default) =>
            Task.FromResult(Blobs.ContainsKey(blobName));

        public Task<bool> DeleteAsync(string blobName, CancellationToken cancellationToken = default)
        {
            DeleteCalls.Add(blobName);
            if (FailDeletes || FailDeletesFor.Contains(blobName)) throw new InvalidOperationException("storage down: AccountKey=super-secret");
            return Task.FromResult(Blobs.Remove(blobName)); // false = already gone
        }

        public Task DeleteByPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    internal static class AttachmentTestSupport
    {
        public static ClaimsPrincipal Student(Guid id) => Principal(id, "Student");

        public static ClaimsPrincipal Admin(Guid id) => Principal(id, "Admin");

        private static ClaimsPrincipal Principal(Guid id, string role) =>
            new(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, id.ToString()),
                new Claim(ClaimTypes.Role, role),
            }, authenticationType: "test"));

        public static Mock<IHubContext<ChatHub>> Hub()
        {
            var proxy = new Mock<IClientProxy>();
            proxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var clients = new Mock<IHubClients>();
            clients.Setup(c => c.Group(It.IsAny<string>())).Returns(proxy.Object);
            clients.Setup(c => c.Groups(It.IsAny<IReadOnlyList<string>>())).Returns(proxy.Object);
            var hub = new Mock<IHubContext<ChatHub>>();
            hub.SetupGet(h => h.Clients).Returns(clients.Object);
            return hub;
        }

        public static ControllerContext Context(ClaimsPrincipal user) =>
            new() { HttpContext = new DefaultHttpContext { User = user } };

        // Evaluates a controller's/action's real [Authorize] metadata through ASP.NET Core's authorization
        // service -- the same evaluation the framework performs before an action runs.
        public static async Task<bool> IsAllowedAsync(Type controller, string actionName, ClaimsPrincipal user)
        {
            var services = new ServiceCollection().AddLogging().AddAuthorization().BuildServiceProvider();
            var authz = services.GetRequiredService<IAuthorizationService>();
            var provider = new DefaultAuthorizationPolicyProvider(services.GetRequiredService<IOptions<AuthorizationOptions>>());

            var action = controller.GetMethod(actionName)!;
            Assert.Empty(action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
            Assert.Empty(controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));

            var authorizeData = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                .Concat(action.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
                .Cast<IAuthorizeData>().ToList();
            Assert.NotEmpty(authorizeData); // no [Authorize] at all would mean anonymous access: fail loudly

            var policy = await AuthorizationPolicy.CombineAsync(provider, authorizeData);
            var result = await authz.AuthorizeAsync(user, resource: null, policy!.Requirements);
            return result.Succeeded;
        }
    }

    // Authorization of the private attachment endpoints:
    //   GET /api/chat/messages/{id}/attachment            (active, non-banned member of the message's room)
    //   GET /api/directmessages/messages/{messageId}/attachment   (one of the two participants)
    // Authorization is evaluated from the STORED message before storage is touched, and everything that is
    // not allowed is a bare 404 (no existence leak).
    public class PrivateAttachmentEndpointTests : IDisposable
    {
        private readonly string _dbName = Guid.NewGuid().ToString();
        private readonly Guid _alice = Guid.NewGuid();   // sender / member / participant
        private readonly Guid _bob = Guid.NewGuid();     // second DM participant
        private readonly Guid _stranger = Guid.NewGuid();
        private readonly Guid _banned = Guid.NewGuid();
        private readonly Guid _roomId = Guid.NewGuid();
        private readonly Guid _conversationId = Guid.NewGuid();

        private const string ChatPng = "/uploads/chat-attachments/11111111-1111-1111-1111-111111111111.png";
        private const string ChatPdf = "/uploads/chat-attachments/11111111-1111-1111-1111-111111111112.pdf";
        private const string DmWebm = "/uploads/dm-attachments/22222222-2222-2222-2222-222222222222.webm";

        private ScoramDbContext NewContext() => new(
            new DbContextOptionsBuilder<ScoramDbContext>().UseInMemoryDatabase(_dbName).Options);

        public void Dispose()
        {
            using var db = NewContext();
            db.Database.EnsureDeleted();
        }

        private ChatController NewChat(ScoramDbContext db, Guid caller)
        {
            return new ChatController(db, Mock.Of<IFileStorageService>(), AttachmentTestSupport.Hub().Object,
                Mock.Of<INotificationService>(), Mock.Of<IChatPresenceService>(), Mock.Of<IMyExamScopeService>())
            {
                ControllerContext = AttachmentTestSupport.Context(AttachmentTestSupport.Student(caller)),
            };
        }

        private DirectMessagesController NewDm(ScoramDbContext db, Guid caller)
        {
            return new DirectMessagesController(db, Mock.Of<IFileStorageService>(), AttachmentTestSupport.Hub().Object,
                Mock.Of<INotificationService>(), Mock.Of<IDmPresenceService>(), Mock.Of<IDmViewingService>())
            {
                ControllerContext = AttachmentTestSupport.Context(AttachmentTestSupport.Student(caller)),
            };
        }

        private async Task<Guid> SeedChatMessageAsync(string? url, bool deleted = false)
        {
            using var db = NewContext();
            if (!await db.ChatRoomMemberships.AnyAsync(m => m.ChatRoomId == _roomId))
            {
                db.ChatRoomMemberships.Add(new ChatRoomMembership { ChatRoomId = _roomId, UserId = _alice });
                db.ChatRoomMemberships.Add(new ChatRoomMembership { ChatRoomId = _roomId, UserId = _banned, IsBanned = true });
            }
            var message = new ChatMessage
            {
                ChatRoomId = _roomId, UserId = _alice, MessageType = ChatMessageType.Image,
                AttachmentUrl = url, IsDeleted = deleted,
            };
            db.ChatMessages.Add(message);
            await db.SaveChangesAsync();
            return message.Id;
        }

        private async Task<Guid> SeedDmMessageAsync(string? url, bool deleted = false)
        {
            using var db = NewContext();
            if (!await db.DirectConversations.AnyAsync(c => c.Id == _conversationId))
            {
                db.DirectConversations.Add(new DirectConversation { Id = _conversationId, UserAId = _alice, UserBId = _bob });
                // The message list Include()s the sender; a required navigation is an inner join, so the sender must exist.
                db.Users.Add(new User { Id = _alice, Username = "alice", Email = "alice@example.com", FullName = "Alice", PasswordHash = "x" });
            }
            var message = new DirectMessage
            {
                ConversationId = _conversationId, SenderId = _alice, MessageType = DirectMessageType.Audio,
                AttachmentUrl = url, IsDeleted = deleted,
            };
            db.DirectMessages.Add(message);
            await db.SaveChangesAsync();
            return message.Id;
        }

        // ---- the endpoints require a signed-in student ------------------------------------------

        [Theory]
        [InlineData(typeof(ChatController))]
        [InlineData(typeof(DirectMessagesController))]
        public async Task Anonymous_and_non_student_callers_are_rejected_by_the_authorization_pipeline(Type controller)
        {
            var anonymous = new ClaimsPrincipal(new ClaimsIdentity()); // no authentication type = unauthenticated

            Assert.False(await AttachmentTestSupport.IsAllowedAsync(controller, "GetAttachment", anonymous));
            Assert.False(await AttachmentTestSupport.IsAllowedAsync(controller, "GetAttachment", AttachmentTestSupport.Admin(Guid.NewGuid())));
            Assert.True(await AttachmentTestSupport.IsAllowedAsync(controller, "GetAttachment", AttachmentTestSupport.Student(Guid.NewGuid())));
        }

        // ---- group chat ------------------------------------------------------------------------

        [Fact]
        public async Task Group_active_member_receives_the_attachment_with_private_headers()
        {
            var id = await SeedChatMessageAsync(ChatPng);
            var store = new FakeBlobStore().With("chat-attachments/11111111-1111-1111-1111-111111111111.png");
            using var db = NewContext();
            var controller = NewChat(db, _alice);

            var result = await controller.GetAttachment(id, store, CancellationToken.None);

            var file = Assert.IsType<FileStreamResult>(result);
            Assert.Equal("image/png", file.ContentType);
            var headers = controller.Response.Headers;
            Assert.Equal("private, no-store", headers.CacheControl.ToString());
            Assert.Equal("nosniff", headers["X-Content-Type-Options"].ToString());
            Assert.StartsWith("inline", headers.ContentDisposition.ToString());
            Assert.DoesNotContain("11111111", headers.ContentDisposition.ToString()); // blob name is not revealed
            Assert.Equal(new[] { "chat-attachments/11111111-1111-1111-1111-111111111111.png" }, store.DownloadCalls);
        }

        [Fact]
        public async Task Group_document_is_delivered_as_a_download()
        {
            var id = await SeedChatMessageAsync(ChatPdf);
            var store = new FakeBlobStore().With("chat-attachments/11111111-1111-1111-1111-111111111112.pdf");
            using var db = NewContext();
            var controller = NewChat(db, _alice);

            var result = await controller.GetAttachment(id, store, CancellationToken.None);

            Assert.Equal("application/pdf", Assert.IsType<FileStreamResult>(result).ContentType);
            Assert.StartsWith("attachment", controller.Response.Headers.ContentDisposition.ToString());
        }

        [Theory]
        [InlineData("stranger")]
        [InlineData("banned")]
        public async Task Group_non_member_and_banned_member_get_404_and_storage_is_never_touched(string who)
        {
            var id = await SeedChatMessageAsync(ChatPng);
            var store = new FakeBlobStore().With("chat-attachments/11111111-1111-1111-1111-111111111111.png");
            using var db = NewContext();
            var caller = who == "banned" ? _banned : _stranger;

            var result = await NewChat(db, caller).GetAttachment(id, store, CancellationToken.None);

            Assert.IsType<NotFoundResult>(result);
            Assert.Empty(store.DownloadCalls); // authorization happened BEFORE any blob access
        }

        [Fact]
        public async Task Group_missing_deleted_or_attachmentless_messages_are_404()
        {
            var deleted = await SeedChatMessageAsync(ChatPng, deleted: true);
            var noAttachment = await SeedChatMessageAsync(null);
            var store = new FakeBlobStore().With("chat-attachments/11111111-1111-1111-1111-111111111111.png");
            using var db = NewContext();
            var controller = NewChat(db, _alice);

            Assert.IsType<NotFoundResult>(await controller.GetAttachment(Guid.NewGuid(), store, CancellationToken.None));
            Assert.IsType<NotFoundResult>(await controller.GetAttachment(deleted, store, CancellationToken.None));
            Assert.IsType<NotFoundResult>(await controller.GetAttachment(noAttachment, store, CancellationToken.None));
            Assert.Empty(store.DownloadCalls);
        }

        [Fact]
        public async Task Group_message_whose_stored_value_points_at_a_public_asset_is_never_read()
        {
            // Defence in depth: even a member cannot use this endpoint to read a blob outside chat-attachments.
            var id = await SeedChatMessageAsync("/uploads/avatars/33333333-3333-3333-3333-333333333333.png");
            var store = new FakeBlobStore().With("avatars/33333333-3333-3333-3333-333333333333.png");
            using var db = NewContext();

            var result = await NewChat(db, _alice).GetAttachment(id, store, CancellationToken.None);

            Assert.IsType<NotFoundResult>(result);
            Assert.Empty(store.DownloadCalls);
        }

        [Fact]
        public async Task Group_missing_blob_is_404_and_storage_failure_is_a_bare_502_without_details()
        {
            var id = await SeedChatMessageAsync(ChatPng);
            using var db = NewContext();

            var missing = await NewChat(db, _alice).GetAttachment(id, new FakeBlobStore(), CancellationToken.None);
            Assert.IsType<NotFoundResult>(missing);

            var failing = await NewChat(db, _alice).GetAttachment(id, new FakeBlobStore { FailDownloads = true }, CancellationToken.None);
            var status = Assert.IsType<StatusCodeResult>(failing); // StatusCodeResult has no body at all
            Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
        }

        // ---- direct messages --------------------------------------------------------------------

        [Theory]
        [InlineData("alice")]
        [InlineData("bob")]
        public async Task Dm_both_participants_receive_the_attachment(string who)
        {
            var id = await SeedDmMessageAsync(DmWebm);
            var store = new FakeBlobStore().With("dm-attachments/22222222-2222-2222-2222-222222222222.webm");
            using var db = NewContext();
            var controller = NewDm(db, who == "alice" ? _alice : _bob);

            var result = await controller.GetAttachment(id, store, CancellationToken.None);

            Assert.Equal("audio/webm", Assert.IsType<FileStreamResult>(result).ContentType);
            Assert.Equal("private, no-store", controller.Response.Headers.CacheControl.ToString());
            Assert.StartsWith("inline", controller.Response.Headers.ContentDisposition.ToString());
        }

        [Fact]
        public async Task Dm_non_participant_gets_404_and_storage_is_never_touched()
        {
            var id = await SeedDmMessageAsync(DmWebm);
            var store = new FakeBlobStore().With("dm-attachments/22222222-2222-2222-2222-222222222222.webm");
            using var db = NewContext();

            var result = await NewDm(db, _stranger).GetAttachment(id, store, CancellationToken.None);

            Assert.IsType<NotFoundResult>(result);
            Assert.Empty(store.DownloadCalls);
        }

        [Fact]
        public async Task Dm_missing_deleted_attachmentless_and_orphaned_messages_are_404()
        {
            var deleted = await SeedDmMessageAsync(DmWebm, deleted: true);
            var noAttachment = await SeedDmMessageAsync(null);
            Guid orphan;
            using (var seed = NewContext())
            {
                var m = new DirectMessage { ConversationId = Guid.NewGuid(), SenderId = _alice, AttachmentUrl = DmWebm };
                seed.DirectMessages.Add(m); // its conversation row does not exist
                await seed.SaveChangesAsync();
                orphan = m.Id;
            }
            var store = new FakeBlobStore().With("dm-attachments/22222222-2222-2222-2222-222222222222.webm");
            using var db = NewContext();
            var controller = NewDm(db, _alice);

            Assert.IsType<NotFoundResult>(await controller.GetAttachment(Guid.NewGuid(), store, CancellationToken.None));
            Assert.IsType<NotFoundResult>(await controller.GetAttachment(deleted, store, CancellationToken.None));
            Assert.IsType<NotFoundResult>(await controller.GetAttachment(noAttachment, store, CancellationToken.None));
            Assert.IsType<NotFoundResult>(await controller.GetAttachment(orphan, store, CancellationToken.None));
            Assert.Empty(store.DownloadCalls);
        }

        [Fact]
        public async Task Dm_message_pointing_at_the_wrong_folder_is_never_read()
        {
            var id = await SeedDmMessageAsync("/uploads/chat-attachments/11111111-1111-1111-1111-111111111111.png");
            var store = new FakeBlobStore().With("chat-attachments/11111111-1111-1111-1111-111111111111.png");
            using var db = NewContext();

            var result = await NewDm(db, _alice).GetAttachment(id, store, CancellationToken.None);

            Assert.IsType<NotFoundResult>(result);
            Assert.Empty(store.DownloadCalls);
        }

        [Fact]
        public async Task Dm_missing_blob_is_404_and_storage_failure_is_a_bare_502()
        {
            var id = await SeedDmMessageAsync(DmWebm);
            using var db = NewContext();

            Assert.IsType<NotFoundResult>(await NewDm(db, _alice).GetAttachment(id, new FakeBlobStore(), CancellationToken.None));
            var failing = await NewDm(db, _alice).GetAttachment(id, new FakeBlobStore { FailDownloads = true }, CancellationToken.None);
            Assert.Equal(StatusCodes.Status502BadGateway, Assert.IsType<StatusCodeResult>(failing).StatusCode);
        }

        // ---- message payloads no longer carry the permanent "/uploads/..." URL ------------------

        [Fact]
        public async Task Dm_message_list_exposes_the_authenticated_path_not_the_stored_upload_url()
        {
            var id = await SeedDmMessageAsync(DmWebm);
            using var db = NewContext();

            var result = await NewDm(db, _alice).GetMessages(_conversationId, null);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var dto = Assert.Single(Assert.IsType<List<DirectMessageResponseDto>>(ok.Value));
            Assert.Equal($"/api/directmessages/messages/{id}/attachment", dto.AttachmentUrl);
            Assert.DoesNotContain("/uploads/", dto.AttachmentUrl);
        }

        [Fact]
        public async Task Dm_message_list_for_a_non_participant_is_still_forbidden()
        {
            await SeedDmMessageAsync(DmWebm);
            using var db = NewContext();

            var result = await NewDm(db, _stranger).GetMessages(_conversationId, null);

            Assert.IsType<ForbidResult>(result.Result);
        }

        [Fact]
        public async Task Group_message_list_exposes_the_authenticated_path_and_enforces_membership()
        {
            var id = await SeedChatMessageAsync(ChatPng);
            using var db = NewContext();

            var ok = Assert.IsType<OkObjectResult>((await NewChat(db, _alice).GetMessages(_roomId, null)).Result);
            var dto = Assert.Single(Assert.IsType<List<ChatMessageResponseDto>>(ok.Value));
            Assert.Equal($"/api/chat/messages/{id}/attachment", dto.AttachmentUrl);
            Assert.DoesNotContain("/uploads/", dto.AttachmentUrl);

            Assert.IsType<ForbidResult>((await NewChat(db, _stranger).GetMessages(_roomId, null)).Result);
            Assert.IsType<ForbidResult>((await NewChat(db, _banned).GetMessages(_roomId, null)).Result);
        }

        [Fact]
        public void Mapped_dto_of_a_deleted_message_has_no_attachment_url()
        {
            var message = new ChatMessage { ChatRoomId = _roomId, UserId = _alice, AttachmentUrl = ChatPng, IsDeleted = true };

            Assert.Null(ChatController.MapMessage(message, _alice).AttachmentUrl);
        }
    }

    // "Delete for everyone" / unsend / moderator delete must also remove the private blob -- and do so
    // idempotently, without ever touching a public asset, and without turning a storage failure into an
    // error for the user. Recovery model under test: the message is hidden first; the stored URL is
    // cleared only once the blob is confirmed gone, so a repeated request finishes a failed cleanup.
    public class PrivateAttachmentDeletionTests : IDisposable
    {
        private readonly string _dbName = Guid.NewGuid().ToString();
        private readonly Guid _alice = Guid.NewGuid();
        private readonly Guid _bob = Guid.NewGuid();
        private readonly Guid _roomId = Guid.NewGuid();
        private readonly Guid _conversationId = Guid.NewGuid();

        private const string ChatBlob = "chat-attachments/11111111-1111-1111-1111-111111111111.png";
        private const string ChatUrl = "/uploads/" + ChatBlob;
        private const string DmBlob = "dm-attachments/22222222-2222-2222-2222-222222222222.webm";
        private const string DmUrl = "/uploads/" + DmBlob;

        private ScoramDbContext NewContext() => new(
            new DbContextOptionsBuilder<ScoramDbContext>().UseInMemoryDatabase(_dbName).Options);

        public void Dispose()
        {
            using var db = NewContext();
            db.Database.EnsureDeleted();
        }

        private ChatController NewChat(ScoramDbContext db, Guid caller) =>
            new(db, Mock.Of<IFileStorageService>(), AttachmentTestSupport.Hub().Object,
                Mock.Of<INotificationService>(), Mock.Of<IChatPresenceService>(), Mock.Of<IMyExamScopeService>())
            {
                ControllerContext = AttachmentTestSupport.Context(AttachmentTestSupport.Student(caller)),
            };

        private DirectMessagesController NewDm(ScoramDbContext db, Guid caller) =>
            new(db, Mock.Of<IFileStorageService>(), AttachmentTestSupport.Hub().Object,
                Mock.Of<INotificationService>(), Mock.Of<IDmPresenceService>(), Mock.Of<IDmViewingService>())
            {
                ControllerContext = AttachmentTestSupport.Context(AttachmentTestSupport.Student(caller)),
            };

        private async Task<Guid> SeedChatAsync(string? url)
        {
            using var db = NewContext();
            var m = new ChatMessage { ChatRoomId = _roomId, UserId = _alice, MessageType = ChatMessageType.Image, AttachmentUrl = url };
            db.ChatMessages.Add(m);
            await db.SaveChangesAsync();
            return m.Id;
        }

        private async Task<Guid> SeedDmAsync(string? url)
        {
            using var db = NewContext();
            if (!await db.DirectConversations.AnyAsync(c => c.Id == _conversationId))
                db.DirectConversations.Add(new DirectConversation { Id = _conversationId, UserAId = _alice, UserBId = _bob });
            var m = new DirectMessage
            {
                ConversationId = _conversationId, SenderId = _alice, MessageType = DirectMessageType.Audio,
                AttachmentUrl = url, MessageText = "caption",
            };
            db.DirectMessages.Add(m);
            await db.SaveChangesAsync();
            return m.Id;
        }

        private async Task<ChatMessage> ChatRow(Guid id)
        {
            using var db = NewContext();
            return await db.ChatMessages.AsNoTracking().SingleAsync(m => m.Id == id);
        }

        private async Task<DirectMessage> DmRow(Guid id)
        {
            using var db = NewContext();
            return await db.DirectMessages.AsNoTracking().SingleAsync(m => m.Id == id);
        }

        // ---- group chat --------------------------------------------------------------------------

        [Fact]
        public async Task Group_delete_removes_the_blob_and_clears_the_url()
        {
            var id = await SeedChatAsync(ChatUrl);
            var store = new FakeBlobStore().With(ChatBlob).With("avatars/keep-me.png");
            using (var db = NewContext())
                Assert.IsType<NoContentResult>(await NewChat(db, _alice).DeleteOwnMessage(id, store));

            var row = await ChatRow(id);
            Assert.True(row.IsDeleted);
            Assert.Null(row.AttachmentUrl);
            Assert.Equal(new[] { ChatBlob }, store.DeleteCalls);
            Assert.False(store.Blobs.ContainsKey(ChatBlob));
            Assert.True(store.Blobs.ContainsKey("avatars/keep-me.png")); // public assets are untouched
        }

        [Fact]
        public async Task Group_delete_of_a_text_message_succeeds_without_any_storage_call()
        {
            var id = await SeedChatAsync(null);
            var store = new FakeBlobStore();
            using (var db = NewContext())
                Assert.IsType<NoContentResult>(await NewChat(db, _alice).DeleteOwnMessage(id, store));

            Assert.True((await ChatRow(id)).IsDeleted);
            Assert.Empty(store.DeleteCalls);
        }

        [Fact]
        public async Task Group_delete_by_someone_else_is_forbidden_and_changes_nothing()
        {
            var id = await SeedChatAsync(ChatUrl);
            var store = new FakeBlobStore().With(ChatBlob);
            using (var db = NewContext())
                Assert.IsType<ForbidResult>(await NewChat(db, _bob).DeleteOwnMessage(id, store));

            var row = await ChatRow(id);
            Assert.False(row.IsDeleted);
            Assert.Equal(ChatUrl, row.AttachmentUrl);
            Assert.Empty(store.DeleteCalls);
            Assert.True(store.Blobs.ContainsKey(ChatBlob));
        }

        [Fact]
        public async Task Group_delete_of_an_unknown_message_is_404()
        {
            var store = new FakeBlobStore();
            using var db = NewContext();

            Assert.IsType<NotFoundResult>(await NewChat(db, _alice).DeleteOwnMessage(Guid.NewGuid(), store));
            Assert.Empty(store.DeleteCalls);
        }

        [Fact]
        public async Task Group_delete_with_the_blob_already_missing_still_succeeds_and_clears_the_url()
        {
            var id = await SeedChatAsync(ChatUrl);
            var store = new FakeBlobStore(); // blob does not exist
            using (var db = NewContext())
                Assert.IsType<NoContentResult>(await NewChat(db, _alice).DeleteOwnMessage(id, store));

            Assert.Null((await ChatRow(id)).AttachmentUrl);
            Assert.Single(store.DeleteCalls);
        }

        [Fact]
        public async Task Group_repeated_delete_is_idempotent()
        {
            var id = await SeedChatAsync(ChatUrl);
            var store = new FakeBlobStore().With(ChatBlob);
            for (var i = 0; i < 3; i++)
            {
                using var db = NewContext();
                Assert.IsType<NoContentResult>(await NewChat(db, _alice).DeleteOwnMessage(id, store));
            }

            var row = await ChatRow(id);
            Assert.True(row.IsDeleted);
            Assert.Null(row.AttachmentUrl);
            Assert.Single(store.DeleteCalls); // later repeats have nothing left to delete
        }

        [Fact]
        public async Task Group_storage_failure_hides_the_message_keeps_the_url_and_a_retry_finishes_the_job()
        {
            var id = await SeedChatAsync(ChatUrl);
            var store = new FakeBlobStore().With(ChatBlob);

            store.FailDeletes = true;
            using (var db = NewContext())
            {
                // The user still gets a normal success: the message IS deleted for everyone.
                Assert.IsType<NoContentResult>(await NewChat(db, _alice).DeleteOwnMessage(id, store));
            }
            var afterFailure = await ChatRow(id);
            Assert.True(afterFailure.IsDeleted);
            Assert.Equal(ChatUrl, afterFailure.AttachmentUrl); // kept as the retry marker; never exposed (DTO hides it)
            Assert.Null(ChatController.MapMessage(afterFailure, _alice).AttachmentUrl);
            Assert.True(store.Blobs.ContainsKey(ChatBlob));

            store.FailDeletes = false;
            using (var db = NewContext())
                Assert.IsType<NoContentResult>(await NewChat(db, _alice).DeleteOwnMessage(id, store));

            var afterRetry = await ChatRow(id);
            Assert.True(afterRetry.IsDeleted);
            Assert.Null(afterRetry.AttachmentUrl);
            Assert.False(store.Blobs.ContainsKey(ChatBlob));
        }

        [Theory]
        [InlineData("/uploads/avatars/33333333-3333-3333-3333-333333333333.png")]
        [InlineData("/uploads/dm-attachments/22222222-2222-2222-2222-222222222222.webm")] // another conversation's private folder
        [InlineData("/uploads/chat-attachments/../avatars/a.png")]
        [InlineData("https://example.com/other.png")]
        public async Task Group_delete_never_deletes_a_blob_outside_the_chat_attachment_folder(string stored)
        {
            var id = await SeedChatAsync(stored);
            var store = new FakeBlobStore().With("avatars/33333333-3333-3333-3333-333333333333.png").With(DmBlob);
            using (var db = NewContext())
                Assert.IsType<NoContentResult>(await NewChat(db, _alice).DeleteOwnMessage(id, store));

            Assert.Empty(store.DeleteCalls);
            Assert.Equal(2, store.Blobs.Count);
            Assert.True((await ChatRow(id)).IsDeleted);
        }

        // ---- direct messages --------------------------------------------------------------------

        [Fact]
        public async Task Dm_unsend_removes_the_blob_wipes_the_text_and_clears_the_url()
        {
            var id = await SeedDmAsync(DmUrl);
            var store = new FakeBlobStore().With(DmBlob).With("exam-logos/keep-me.png");
            using (var db = NewContext())
                Assert.IsType<NoContentResult>(await NewDm(db, _alice).DeleteMessage(id, store));

            var row = await DmRow(id);
            Assert.True(row.IsDeleted);
            Assert.Null(row.MessageText);
            Assert.Null(row.AttachmentUrl);
            Assert.Equal(new[] { DmBlob }, store.DeleteCalls);
            Assert.True(store.Blobs.ContainsKey("exam-logos/keep-me.png"));
        }

        [Fact]
        public async Task Dm_unsend_of_a_text_message_needs_no_storage_call()
        {
            var id = await SeedDmAsync(null);
            var store = new FakeBlobStore();
            using (var db = NewContext())
                Assert.IsType<NoContentResult>(await NewDm(db, _alice).DeleteMessage(id, store));

            Assert.True((await DmRow(id)).IsDeleted);
            Assert.Empty(store.DeleteCalls);
        }

        [Fact]
        public async Task Dm_unsend_by_the_other_participant_is_forbidden_and_deletes_nothing()
        {
            var id = await SeedDmAsync(DmUrl);
            var store = new FakeBlobStore().With(DmBlob);
            using (var db = NewContext())
                Assert.IsType<ForbidResult>(await NewDm(db, _bob).DeleteMessage(id, store));

            var row = await DmRow(id);
            Assert.False(row.IsDeleted);
            Assert.Equal(DmUrl, row.AttachmentUrl);
            Assert.Empty(store.DeleteCalls);
        }

        [Fact]
        public async Task Dm_repeated_unsend_and_missing_blob_are_handled_safely()
        {
            var id = await SeedDmAsync(DmUrl);
            var store = new FakeBlobStore(); // blob already gone
            for (var i = 0; i < 2; i++)
            {
                using var db = NewContext();
                Assert.IsType<NoContentResult>(await NewDm(db, _alice).DeleteMessage(id, store));
            }

            var row = await DmRow(id);
            Assert.True(row.IsDeleted);
            Assert.Null(row.AttachmentUrl);
            Assert.Single(store.DeleteCalls);
        }

        [Fact]
        public async Task Dm_storage_failure_keeps_the_marker_and_a_retry_finishes_the_job()
        {
            var id = await SeedDmAsync(DmUrl);
            var store = new FakeBlobStore().With(DmBlob);
            store.FailDeletes = true;

            using (var db = NewContext())
                Assert.IsType<NoContentResult>(await NewDm(db, _alice).DeleteMessage(id, store));
            var afterFailure = await DmRow(id);
            Assert.True(afterFailure.IsDeleted);
            Assert.Null(afterFailure.MessageText);
            Assert.Equal(DmUrl, afterFailure.AttachmentUrl);
            Assert.True(store.Blobs.ContainsKey(DmBlob));

            store.FailDeletes = false;
            using (var db = NewContext())
                Assert.IsType<NoContentResult>(await NewDm(db, _alice).DeleteMessage(id, store));

            Assert.Null((await DmRow(id)).AttachmentUrl);
            Assert.False(store.Blobs.ContainsKey(DmBlob));
        }

        [Theory]
        [InlineData("/uploads/avatars/33333333-3333-3333-3333-333333333333.png")]
        [InlineData("/uploads/chat-attachments/11111111-1111-1111-1111-111111111111.png")]
        [InlineData("/uploads/dm-attachments/sub/a.png")]
        public async Task Dm_unsend_never_deletes_a_blob_outside_the_dm_attachment_folder(string stored)
        {
            var id = await SeedDmAsync(stored);
            var store = new FakeBlobStore().With("avatars/33333333-3333-3333-3333-333333333333.png").With(ChatBlob);
            using (var db = NewContext())
                Assert.IsType<NoContentResult>(await NewDm(db, _alice).DeleteMessage(id, store));

            Assert.Empty(store.DeleteCalls);
            Assert.Equal(2, store.Blobs.Count);
        }

        // ---- moderator delete (AdminChatController.ResolveReport) --------------------------------

        [Fact]
        public async Task Moderator_delete_of_a_reported_message_also_removes_its_blob()
        {
            var messageId = await SeedChatAsync(ChatUrl);
            var reportId = Guid.NewGuid();
            using (var seed = NewContext())
            {
                seed.ChatReports.Add(new ChatReport { Id = reportId, ChatMessageId = messageId, ReportedByUserId = _bob, Reason = "spam" });
                await seed.SaveChangesAsync();
            }
            var permissions = new Mock<IAdminPermissionService>();
            permissions.Setup(p => p.HasPermissionAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<AdminPermission>())).ReturnsAsync(true);
            var store = new FakeBlobStore().With(ChatBlob);

            using (var db = NewContext())
            {
                var controller = new AdminChatController(db, permissions.Object, AttachmentTestSupport.Hub().Object, Mock.Of<IFileStorageService>())
                {
                    ControllerContext = AttachmentTestSupport.Context(AttachmentTestSupport.Admin(Guid.NewGuid())),
                };

                var result = await controller.ResolveReport(
                    reportId,
                    new ChatReportResolveDto { Status = "ActionTaken", DeleteMessage = true },
                    store);

                Assert.IsType<OkObjectResult>(result);
            }

            var row = await ChatRow(messageId);
            Assert.True(row.IsDeleted);
            Assert.Null(row.AttachmentUrl);
            Assert.Equal(new[] { ChatBlob }, store.DeleteCalls);
        }

        [Fact]
        public async Task Moderator_dismissal_without_deletion_leaves_the_message_and_blob_alone()
        {
            var messageId = await SeedChatAsync(ChatUrl);
            var reportId = Guid.NewGuid();
            using (var seed = NewContext())
            {
                seed.ChatReports.Add(new ChatReport { Id = reportId, ChatMessageId = messageId, ReportedByUserId = _bob, Reason = "spam" });
                await seed.SaveChangesAsync();
            }
            var permissions = new Mock<IAdminPermissionService>();
            permissions.Setup(p => p.HasPermissionAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<AdminPermission>())).ReturnsAsync(true);
            var store = new FakeBlobStore().With(ChatBlob);

            using (var db = NewContext())
            {
                var controller = new AdminChatController(db, permissions.Object, AttachmentTestSupport.Hub().Object, Mock.Of<IFileStorageService>())
                {
                    ControllerContext = AttachmentTestSupport.Context(AttachmentTestSupport.Admin(Guid.NewGuid())),
                };
                await controller.ResolveReport(reportId, new ChatReportResolveDto { Status = "Dismissed", DeleteMessage = false }, store);
            }

            var row = await ChatRow(messageId);
            Assert.False(row.IsDeleted);
            Assert.Equal(ChatUrl, row.AttachmentUrl);
            Assert.Empty(store.DeleteCalls);
        }
    }
}
