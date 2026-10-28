using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScoramAPI.Controllers;
using ScoramAPI.Data;
using ScoramAPI.Enums;
using ScoramAPI.Models;
using ScoramAPI.Services;
using Xunit;

namespace ScoramAPI.Tests.Controllers
{
    // Collects everything a logger is asked to write -- the formatted message AND the exception text -- so a
    // test can prove that storage failures never put a blob name, URL or credential into the logs.
    internal sealed class CapturingLogger : ILogger
    {
        public List<string> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception) + (exception == null ? "" : " | " + exception));
    }

    // The retry path for blob deletions that failed at delete time. A message that was "deleted for everyone"
    // is hidden, so its sender cannot repeat the request; the sweep is what finishes the job. Blob deletes use
    // an in-memory fake store only -- no real storage is ever contacted.
    public class PrivateAttachmentCleanupSweepTests : IDisposable
    {
        private readonly string _dbName = Guid.NewGuid().ToString();
        private readonly Guid _user = Guid.NewGuid();
        private readonly Guid _room = Guid.NewGuid();
        private readonly Guid _conversation = Guid.NewGuid();

        private static string ChatBlob(string n) => $"chat-attachments/{n}.png";
        private static string DmBlob(string n) => $"dm-attachments/{n}.webm";
        private static string Url(string blob) => "/uploads/" + blob;

        private ScoramDbContext NewContext() => new(new DbContextOptionsBuilder<ScoramDbContext>().UseInMemoryDatabase(_dbName).Options);

        public void Dispose()
        {
            using var db = NewContext();
            db.Database.EnsureDeleted();
        }

        private async Task<Guid> SeedChatAsync(string? url, bool deleted, Guid? id = null)
        {
            using var db = NewContext();
            var m = new ChatMessage { ChatRoomId = _room, UserId = _user, MessageType = ChatMessageType.Image, AttachmentUrl = url, IsDeleted = deleted };
            if (id.HasValue) m.Id = id.Value;
            db.ChatMessages.Add(m);
            await db.SaveChangesAsync();
            return m.Id;
        }

        private async Task<Guid> SeedDmAsync(string? url, bool deleted)
        {
            using var db = NewContext();
            var m = new DirectMessage { ConversationId = _conversation, SenderId = _user, MessageType = DirectMessageType.Audio, AttachmentUrl = url, IsDeleted = deleted };
            db.DirectMessages.Add(m);
            await db.SaveChangesAsync();
            return m.Id;
        }

        private async Task<AttachmentSweepResult> SweepAsync(FakeBlobStore blobs, int pageSize = 100, ILogger? logger = null)
        {
            using var db = NewContext();
            return await PrivateAttachmentCleanupService.SweepOnceAsync(db, blobs, logger, CancellationToken.None, pageSize);
        }

        private async Task<string?> ChatUrlAsync(Guid id) { using var db = NewContext(); return (await db.ChatMessages.FindAsync(id))!.AttachmentUrl; }
        private async Task<string?> DmUrlAsync(Guid id) { using var db = NewContext(); return (await db.DirectMessages.FindAsync(id))!.AttachmentUrl; }

        [Fact]
        public async Task The_sweep_deletes_the_blobs_of_deleted_messages_and_clears_their_markers()
        {
            var chat = await SeedChatAsync(Url(ChatBlob("a1")), deleted: true);
            var dm = await SeedDmAsync(Url(DmBlob("b1")), deleted: true);
            var blobs = new FakeBlobStore().With(ChatBlob("a1")).With(DmBlob("b1"));

            var result = await SweepAsync(blobs);

            Assert.Equal(new AttachmentSweepResult(2, 2, 0), result);
            Assert.Empty(blobs.Blobs);
            Assert.Null(await ChatUrlAsync(chat));
            Assert.Null(await DmUrlAsync(dm));
        }

        [Fact]
        public async Task Messages_that_are_not_deleted_are_never_touched()
        {
            var liveChat = await SeedChatAsync(Url(ChatBlob("live1")), deleted: false);
            var liveDm = await SeedDmAsync(Url(DmBlob("live2")), deleted: false);
            var blobs = new FakeBlobStore().With(ChatBlob("live1")).With(DmBlob("live2"));

            var result = await SweepAsync(blobs);

            Assert.Equal(new AttachmentSweepResult(0, 0, 0), result);
            Assert.Empty(blobs.DeleteCalls);
            Assert.Equal(2, blobs.Blobs.Count);
            Assert.Equal(Url(ChatBlob("live1")), await ChatUrlAsync(liveChat));
            Assert.Equal(Url(DmBlob("live2")), await DmUrlAsync(liveDm));
        }

        // REGRESSION: the person's "delete for everyone" hid the message while storage was down. The marker must
        // survive that failure, and a later sweep (storage healthy again) must finish the deletion.
        [Fact]
        public async Task A_deletion_that_failed_is_retried_by_a_later_sweep_until_it_succeeds()
        {
            var chat = await SeedChatAsync(Url(ChatBlob("a2")), deleted: true);
            var blobs = new FakeBlobStore().With(ChatBlob("a2"));
            blobs.FailDeletes = true;

            var first = await SweepAsync(blobs);

            Assert.Equal(new AttachmentSweepResult(1, 0, 1), first);
            Assert.Equal(Url(ChatBlob("a2")), await ChatUrlAsync(chat)); // the retry marker is still there
            Assert.True(blobs.Blobs.ContainsKey(ChatBlob("a2")));

            blobs.FailDeletes = false; // storage recovered
            var second = await SweepAsync(blobs);

            Assert.Equal(new AttachmentSweepResult(1, 1, 0), second);
            Assert.Empty(blobs.Blobs);
            Assert.Null(await ChatUrlAsync(chat));
        }

        [Fact]
        public async Task A_blob_that_is_already_gone_counts_as_done()
        {
            var dm = await SeedDmAsync(Url(DmBlob("gone")), deleted: true);

            var result = await SweepAsync(new FakeBlobStore());

            Assert.Equal(new AttachmentSweepResult(1, 1, 0), result);
            Assert.Null(await DmUrlAsync(dm));
        }

        [Fact]
        public async Task Sweeping_again_changes_nothing_and_makes_no_storage_calls()
        {
            await SeedChatAsync(Url(ChatBlob("a3")), deleted: true);
            var blobs = new FakeBlobStore().With(ChatBlob("a3"));
            await SweepAsync(blobs);
            var callsAfterFirst = blobs.DeleteCalls.Count;

            var second = await SweepAsync(blobs);

            Assert.Equal(new AttachmentSweepResult(0, 0, 0), second);
            Assert.Equal(callsAfterFirst, blobs.DeleteCalls.Count);
        }

        // A stored value that is not a plain file in the right private folder must never reach storage: a public
        // asset, the OTHER private folder, a traversal attempt or an external URL.
        [Theory]
        [InlineData("/uploads/avatars/11111111-1111-1111-1111-111111111111.png")]
        [InlineData("/uploads/question-images/11111111-1111-1111-1111-111111111111.png")]
        [InlineData("/uploads/dm-attachments/11111111-1111-1111-1111-111111111111.png")]   // chat row pointing at the DM folder
        [InlineData("/uploads/chat-attachments/../avatars/11111111-1111-1111-1111-111111111111.png")]
        [InlineData("https://elsewhere.example/avatars/11111111-1111-1111-1111-111111111111.png")]
        [InlineData("/uploads/chat-attachments/sub/11111111-1111-1111-1111-111111111111.png")]
        public async Task Values_outside_the_private_folder_never_reach_storage_and_never_delete_a_public_asset(string stored)
        {
            var chat = await SeedChatAsync(stored, deleted: true);
            var blobs = new FakeBlobStore()
                .With("avatars/11111111-1111-1111-1111-111111111111.png", "public-avatar")
                .With("question-images/11111111-1111-1111-1111-111111111111.png", "public-question-image")
                .With("dm-attachments/11111111-1111-1111-1111-111111111111.png", "someone-elses-dm");

            var result = await SweepAsync(blobs);

            Assert.Empty(blobs.DeleteCalls);   // storage was never asked to delete anything
            Assert.Equal(3, blobs.Blobs.Count); // nothing else disappeared
            Assert.Equal(1, result.Examined);
            Assert.Null(await ChatUrlAsync(chat)); // the unusable value is dropped so the row is not retried forever
        }

        [Fact]
        public async Task One_failing_blob_does_not_stop_the_others_even_across_pages()
        {
            var ids = new[] { Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.Parse("00000000-0000-0000-0000-000000000002"),
                              Guid.Parse("00000000-0000-0000-0000-000000000003"), Guid.Parse("00000000-0000-0000-0000-000000000004") };
            var blobs = new FakeBlobStore();
            foreach (var (id, n) in ids.Select((id, i) => (id, i)))
            {
                await SeedChatAsync(Url(ChatBlob("p" + n)), deleted: true, id);
                blobs.With(ChatBlob("p" + n));
            }
            blobs.FailDeletesFor.Add(ChatBlob("p0")); // the FIRST row always fails

            var result = await SweepAsync(blobs, pageSize: 1); // one row per page: the failing row must not block the rest

            Assert.Equal(new AttachmentSweepResult(4, 3, 1), result);
            Assert.Equal(new[] { ChatBlob("p0") }, blobs.Blobs.Keys.ToArray());
            using var db = NewContext();
            Assert.Equal(1, await db.ChatMessages.CountAsync(m => m.AttachmentUrl != null)); // only the failed one keeps its marker
        }

        [Fact]
        public async Task Storage_failures_are_logged_without_blob_names_urls_or_credentials()
        {
            await SeedChatAsync(Url(ChatBlob("secret-name")), deleted: true);
            await SeedDmAsync(Url(DmBlob("secret-name")), deleted: true);
            var blobs = new FakeBlobStore().With(ChatBlob("secret-name")).With(DmBlob("secret-name"));
            blobs.FailDeletes = true; // the fake's exception text contains "AccountKey=super-secret"
            var logger = new CapturingLogger();

            await SweepAsync(blobs, logger: logger);

            Assert.NotEmpty(logger.Lines);
            foreach (var line in logger.Lines)
            {
                Assert.DoesNotContain("super-secret", line);
                Assert.DoesNotContain("AccountKey", line);
                Assert.DoesNotContain("secret-name", line);
                Assert.DoesNotContain("/uploads/", line);
                Assert.DoesNotContain("chat-attachments", line);
                Assert.DoesNotContain("dm-attachments", line);
            }
        }

        [Fact]
        public void The_sweep_is_off_unless_explicitly_enabled_by_configuration()
        {
            // Enabling it bulk-deletes the blobs of every already-deleted message that still has a URL -- an
            // irreversible action that must be a deliberate decision, never a side effect of deploying.
            Assert.Equal("PrivateAttachments:CleanupSweep:Enabled", PrivateAttachmentCleanupService.EnabledConfigKey);
            var empty = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
            Assert.False(Microsoft.Extensions.Configuration.ConfigurationBinder.GetValue<bool>(empty, PrivateAttachmentCleanupService.EnabledConfigKey));
        }
    }
}
