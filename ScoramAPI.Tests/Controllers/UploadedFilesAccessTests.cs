using System.Reflection;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ScoramAPI.Controllers;
using ScoramAPI.Middleware;
using ScoramAPI.Services;
using Xunit;

namespace ScoramAPI.Tests.Controllers
{
    // The legacy "/uploads/{folder}/{file}" route after the private-attachment policy:
    //   * PUBLIC folders (question/stimulus images, avatars, exam + organization logos, chat-room icons,
    //     landing media) are still served to anyone, exactly as before;
    //   * the PRIVATE folders (chat-attachments, dm-attachments) and any folder that is not explicitly
    //     public answer a bare 404 WITHOUT touching storage -- so an old permanent URL no longer yields the
    //     file, whether the caller is anonymous or signed in.
    // Private attachments are delivered by the authenticated endpoints tested in
    // PrivateAttachmentEndpointTests.
    public class UploadedFilesAccessTests
    {
        private static UploadedFilesController NewController(Mock<IAzureBlobService> blob, ClaimsPrincipal? user = null)
        {
            return new UploadedFilesController(blob.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = user ?? new ClaimsPrincipal(new ClaimsIdentity()) }
                }
            };
        }

        private static Mock<IAzureBlobService> BlobWith(string blobName, string content = "bytes")
        {
            var blob = new Mock<IAzureBlobService>(MockBehavior.Strict);
            blob.Setup(b => b.DownloadAsync(blobName, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new MemoryStream(Encoding.UTF8.GetBytes(content)));
            return blob;
        }

        private static ClaimsPrincipal Student(Guid id) =>
            new(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, id.ToString()),
                new Claim(ClaimTypes.Role, "Student"),
            }, authenticationType: "test"));

        // ---- route shape ------------------------------------------------------------------------

        [Fact]
        public void Route_stays_at_uploads_and_is_AllowAnonymous_for_public_assets()
        {
            var type = typeof(UploadedFilesController);
            Assert.NotEmpty(type.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
            Assert.Equal("uploads", type.GetCustomAttribute<RouteAttribute>()!.Template);
        }

        // ---- public assets keep working ---------------------------------------------------------

        [Theory]
        [InlineData("question-images", "44444444-4444-4444-4444-444444444444.png", "image/png")]
        [InlineData("stimulus-images", "44444444-4444-4444-4444-444444444445.jpg", "image/jpeg")]
        [InlineData("avatars", "55555555-5555-5555-5555-555555555555.jpg", "image/jpeg")]
        [InlineData("exam-logos", "66666666-6666-6666-6666-666666666666.webp", "image/webp")]
        [InlineData("organization-logos", "77777777-7777-7777-7777-777777777777.png", "image/png")]
        [InlineData("chat-room-icons", "88888888-8888-8888-8888-888888888880.png", "image/png")]
        [InlineData("landing", "88888888-8888-8888-8888-888888888881.webp", "image/webp")]
        public async Task Public_assets_are_served_to_anonymous_callers(string folder, string fileName, string contentType)
        {
            var blob = BlobWith($"{folder}/{fileName}");

            var result = await NewController(blob).Get(folder, fileName);

            var file = Assert.IsType<FileStreamResult>(result);
            Assert.Equal(contentType, file.ContentType);
            blob.Verify(b => b.DownloadAsync($"{folder}/{fileName}", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Public_folder_policy_is_case_insensitive_like_the_route_itself()
        {
            var blob = BlobWith("Avatars/a.png"); // blob keys are case-sensitive and passed through unchanged

            var result = await NewController(blob).Get("Avatars", "a.png");

            Assert.IsType<FileStreamResult>(result);
        }

        // ---- legacy private attachment URLs no longer yield file contents ------------------------

        [Theory]
        [InlineData("chat-attachments", "11111111-1111-1111-1111-111111111111.png")]
        [InlineData("chat-attachments", "11111111-1111-1111-1111-111111111111.pdf")]
        [InlineData("dm-attachments", "22222222-2222-2222-2222-222222222222.jpg")]
        [InlineData("dm-attachments", "22222222-2222-2222-2222-222222222222.webm")]
        [InlineData("Chat-Attachments", "11111111-1111-1111-1111-111111111111.png")]
        [InlineData("DM-ATTACHMENTS", "22222222-2222-2222-2222-222222222222.jpg")]
        public async Task Legacy_private_attachment_urls_are_404_for_anonymous_callers_without_touching_storage(string folder, string fileName)
        {
            var blob = new Mock<IAzureBlobService>(MockBehavior.Strict); // ANY storage call would throw

            var result = await NewController(blob).Get(folder, fileName);

            Assert.IsType<NotFoundResult>(result);
            blob.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData("chat-attachments", "11111111-1111-1111-1111-111111111111.png")]
        [InlineData("dm-attachments", "22222222-2222-2222-2222-222222222222.webm")]
        public async Task Legacy_private_attachment_urls_are_404_even_for_a_signed_in_student(string folder, string fileName)
        {
            var blob = new Mock<IAzureBlobService>(MockBehavior.Strict);

            var result = await NewController(blob, Student(Guid.NewGuid())).Get(folder, fileName);

            Assert.IsType<NotFoundResult>(result);
            blob.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData("profile-images")]
        [InlineData("pyq-pdfs")]
        [InlineData("exam-pdfs")]
        [InlineData("bulk-import-staging")]
        [InlineData("some-new-folder")]
        public async Task Unclassified_folders_are_not_public_by_default(string folder)
        {
            var blob = new Mock<IAzureBlobService>(MockBehavior.Strict);

            var result = await NewController(blob).Get(folder, "33333333-3333-3333-3333-333333333333.png");

            Assert.IsType<NotFoundResult>(result);
            blob.VerifyNoOtherCalls();
        }

        // ---- existing safety behaviour is intact ------------------------------------------------

        [Fact]
        public async Task Missing_public_asset_returns_bare_404()
        {
            var blob = new Mock<IAzureBlobService>(MockBehavior.Strict);
            blob.Setup(b => b.DownloadAsync("question-images/missing.png", It.IsAny<CancellationToken>()))
                .ReturnsAsync((Stream?)null);

            var result = await NewController(blob).Get("question-images", "missing.png");

            Assert.IsType<NotFoundResult>(result); // bodyless (the controller is deliberately not [ApiController])
        }

        [Fact]
        public async Task Storage_failure_on_a_public_asset_returns_502_without_details()
        {
            var blob = new Mock<IAzureBlobService>(MockBehavior.Strict);
            blob.Setup(b => b.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("storage down: AccountKey=secret"));

            var result = await NewController(blob).Get("avatars", "x.png");

            var status = Assert.IsType<StatusCodeResult>(result);
            Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
        }

        [Theory]
        [InlineData("..", "secret.png")]
        [InlineData("avatars", "..")]
        [InlineData("avatars", "a..b.png")]
        [InlineData("chat/attachments", "x.png")]
        [InlineData("chat-attachments", "sub/x.png")]
        [InlineData("chat-attachments", "..")]
        public async Task Path_traversal_shapes_are_rejected_before_touching_storage(string subfolder, string fileName)
        {
            var blob = new Mock<IAzureBlobService>(MockBehavior.Strict);

            var result = await NewController(blob).Get(subfolder, fileName);

            Assert.IsType<BadRequestResult>(result);
            blob.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Public_svg_is_served_with_a_script_blocking_CSP_header()
        {
            var blob = BlobWith("question-images/88888888-8888-8888-8888-888888888888.svg", "<svg/>");
            var controller = NewController(blob);

            var result = await controller.Get("question-images", "88888888-8888-8888-8888-888888888888.svg");

            var file = Assert.IsType<FileStreamResult>(result);
            Assert.Equal("image/svg+xml", file.ContentType);
            Assert.Equal("script-src 'none'; sandbox;", controller.Response.Headers["Content-Security-Policy"].ToString());
        }
    }

    // The same folder policy applied EARLIER in the pipeline, before app.UseStaticFiles(), so a leftover
    // wwwroot/uploads/chat-attachments (or dm-attachments) file on disk cannot be served either.
    public class UploadsPrivacyMiddlewareTests
    {
        [Theory]
        [InlineData("/uploads/chat-attachments/a.png")]
        [InlineData("/uploads/dm-attachments/a.webm")]
        [InlineData("/UPLOADS/Chat-Attachments/a.png")]
        [InlineData("/uploads//dm-attachments/a.png")]
        [InlineData("/uploads/unclassified/a.png")]
        [InlineData("/uploads")]
        [InlineData("/uploads/")]
        // Shapes that reach the file system as the same private file but do not start with "/uploads/" literally.
        [InlineData("//uploads/chat-attachments/a.png")]          // confirmed on Kestrel: StaticFiles trims the extra slash
        [InlineData("///uploads/dm-attachments/a.webm")]
        [InlineData("/./uploads/chat-attachments/a.png")]
        [InlineData("/foo/../uploads/chat-attachments/a.png")]
        [InlineData("/uploads/./chat-attachments/a.png")]
        [InlineData("/uploads/avatars/../chat-attachments/a.png")] // public-looking folder, then climbs into a private one
        [InlineData("/uploads/avatars/..\\chat-attachments\\a.png")]
        [InlineData("/uploads/avatars\\..\\dm-attachments\\a.png")]
        [InlineData("/uploads/avatars/.. /chat-attachments/a.png")] // Windows drops the trailing space
        [InlineData("/uploads/avatars/.../chat-attachments/a.png")]
        [InlineData("/uploads./chat-attachments/a.png")]           // Windows ignores trailing dots / spaces on a segment
        [InlineData("/uploads /chat-attachments/a.png")]
        [InlineData("/uploads/chat-attachments./a.png")]
        [InlineData("/uploads/chat-attachments /a.png")]
        [InlineData("/uploads/chat-attachments%2Fa.png")]          // an encoded slash stays inside the folder segment
        [InlineData("/uploads/%63hat-attachments/a.png")]          // not decoded here -> not a public folder name either
        [InlineData("/uploads\\chat-attachments\\a.png")]
        public void Private_and_unclassified_upload_paths_are_blocked(string path) =>
            Assert.True(UploadsPrivacyMiddleware.IsBlocked(new PathString(path)));

        [Theory]
        [InlineData("/uploads/question-images/a.png")]
        [InlineData("/uploads/stimulus-images/a.png")]
        [InlineData("/uploads/avatars/a.jpg")]
        [InlineData("/uploads/exam-logos/a.png")]
        [InlineData("/uploads/organization-logos/a.png")]
        [InlineData("/uploads/chat-room-icons/a.png")]
        [InlineData("/uploads/landing/a.webp")]
        [InlineData("/api/chat/messages/11111111-1111-1111-1111-111111111111/attachment")]
        [InlineData("/health")]
        [InlineData("/uploadsmore/chat-attachments/a.png")] // not under /uploads at all
        [InlineData("//uploads/avatars/a.png")]            // extra slash is normalised, and avatars IS public
        [InlineData("/UPLOADS/Avatars/a.png")]
        [InlineData("/uploads/avatars/./a.png")]
        public void Public_assets_and_other_routes_are_not_blocked(string path) =>
            Assert.False(UploadsPrivacyMiddleware.IsBlocked(new PathString(path)));

        [Fact]
        public async Task Blocked_request_gets_404_and_never_reaches_the_rest_of_the_pipeline()
        {
            var nextCalled = false;
            var middleware = new UploadsPrivacyMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
            var context = new DefaultHttpContext();
            context.Request.Path = "/uploads/chat-attachments/a.png";

            await middleware.InvokeAsync(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
            Assert.False(nextCalled);
        }

        [Fact]
        public async Task Public_asset_request_passes_through_untouched()
        {
            var nextCalled = false;
            var middleware = new UploadsPrivacyMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
            var context = new DefaultHttpContext();
            context.Request.Path = "/uploads/avatars/a.png";

            await middleware.InvokeAsync(context);

            Assert.True(nextCalled);
            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        }
    }

    // The REAL StaticFileMiddleware reading a REAL wwwroot (a temp folder that still holds leftover private
    // attachment files, like the ones that were found in the source tree), wired in the same order as Program.cs:
    // UploadsPrivacyMiddleware first, then UseStaticFiles, then "everything else" (routing/controllers -> here a 404).
    // Every request is a genuine GET through the pipeline; the assertion is on the bytes that come back.
    public sealed class UploadsPipelineIntegrationTests : IDisposable
    {
        private const string PrivateChatBytes = "PRIVATE-CHAT-BYTES";
        private const string PrivateDmBytes = "PRIVATE-DM-BYTES";
        private const string UnclassifiedBytes = "UNCLASSIFIED-BYTES";
        private const string PublicBytes = "PUBLIC-ASSET-BYTES";

        private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "scoram-uploads-pipeline-" + Guid.NewGuid().ToString("N"));
        private readonly PhysicalFileProvider _files;
        private readonly RequestDelegate _pipeline;

        public UploadsPipelineIntegrationTests()
        {
            Write("uploads/chat-attachments/leftover.png", PrivateChatBytes);
            Write("uploads/dm-attachments/leftover.webm", PrivateDmBytes);
            Write("uploads/some-new-folder/leftover.png", UnclassifiedBytes);
            foreach (var folder in PrivateAttachmentRules.PublicFolders) Write($"uploads/{folder}/asset.png", PublicBytes);

            _files = new PhysicalFileProvider(_webRoot);
            var env = Mock.Of<IWebHostEnvironment>(e => e.WebRootFileProvider == _files);
            RequestDelegate everythingElse = context => { context.Response.StatusCode = StatusCodes.Status404NotFound; return Task.CompletedTask; };
            var staticFiles = new StaticFileMiddleware(
                everythingElse, env, Options.Create(new StaticFileOptions { FileProvider = _files }), NullLoggerFactory.Instance);
            var privacy = new UploadsPrivacyMiddleware(staticFiles.Invoke); // same order as Program.cs
            _pipeline = privacy.InvokeAsync;
        }

        private void Write(string relative, string content)
        {
            var full = Path.Combine(_webRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        public void Dispose()
        {
            _files.Dispose();
            if (Directory.Exists(_webRoot)) Directory.Delete(_webRoot, recursive: true); // the temp folder created above, nothing else
        }

        private async Task<(int Status, string Body)> GetAsync(string path, string? query = null)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = "GET";
            context.Request.Path = PathString.FromUriComponent(path); // decodes %xx like the server does
            if (query != null) context.Request.QueryString = new QueryString(query);
            context.Response.Body = new MemoryStream();

            await _pipeline(context);

            context.Response.Body.Position = 0;
            return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync());
        }

        private static void AssertNoPrivateBytes(string body)
        {
            Assert.DoesNotContain(PrivateChatBytes, body);
            Assert.DoesNotContain(PrivateDmBytes, body);
            Assert.DoesNotContain(UnclassifiedBytes, body);
        }

        [Theory]
        [InlineData("/uploads/chat-attachments/leftover.png")]
        [InlineData("/uploads/dm-attachments/leftover.webm")]
        [InlineData("//uploads/chat-attachments/leftover.png")]
        [InlineData("///uploads/chat-attachments/leftover.png")]
        [InlineData("/uploads//chat-attachments/leftover.png")]
        [InlineData("/./uploads/chat-attachments/leftover.png")]
        [InlineData("/uploads/./dm-attachments/leftover.webm")]
        [InlineData("/foo/../uploads/chat-attachments/leftover.png")]
        [InlineData("/uploads/avatars/../chat-attachments/leftover.png")]
        [InlineData("/uploads/avatars/%2e%2e/chat-attachments/leftover.png")]
        [InlineData("/uploads/avatars%5c..%5cdm-attachments%5cleftover.webm")]
        [InlineData("/UPLOADS/CHAT-ATTACHMENTS/leftover.png")]
        [InlineData("/Uploads/Dm-Attachments/leftover.webm")]
        [InlineData("/uploads/%63hat-attachments/leftover.png")]
        [InlineData("/uploads/chat-attachments/%6cEFTOVER.png")]
        [InlineData("/uploads/chat-attachments./leftover.png")]
        [InlineData("/uploads./chat-attachments/leftover.png")]
        public async Task Leftover_private_attachment_files_in_wwwroot_are_never_served(string path)
        {
            var (status, body) = await GetAsync(path);

            Assert.NotEqual(StatusCodes.Status200OK, status);
            AssertNoPrivateBytes(body);
        }

        [Fact]
        public async Task Query_strings_do_not_change_the_decision_for_a_private_file()
        {
            var (status, body) = await GetAsync("/uploads/chat-attachments/leftover.png", "?v=1&download=true");

            Assert.Equal(StatusCodes.Status404NotFound, status);
            AssertNoPrivateBytes(body);
        }

        [Theory]
        [InlineData("/uploads/some-new-folder/leftover.png")]
        [InlineData("//uploads/some-new-folder/leftover.png")]
        public async Task A_folder_nobody_classified_as_public_is_not_served(string path)
        {
            var (status, body) = await GetAsync(path);

            Assert.NotEqual(StatusCodes.Status200OK, status);
            AssertNoPrivateBytes(body);
        }

        [Fact]
        public async Task Every_public_folder_really_is_served_anonymously_through_the_same_pipeline()
        {
            Assert.NotEmpty(PrivateAttachmentRules.PublicFolders);
            foreach (var folder in PrivateAttachmentRules.PublicFolders)
            {
                var (status, body) = await GetAsync($"/uploads/{folder}/asset.png");
                Assert.Equal(StatusCodes.Status200OK, status);
                Assert.Equal(PublicBytes, body);
            }
        }

        [Theory]
        [InlineData("//uploads/avatars/asset.png")]
        [InlineData("/uploads/avatars/./asset.png")]
        [InlineData("/uploads/question-images/asset.png")]
        public async Task Public_assets_keep_working_for_odd_but_harmless_url_shapes(string path)
        {
            var (status, body) = await GetAsync(path);

            Assert.Equal(StatusCodes.Status200OK, status);
            Assert.Equal(PublicBytes, body);
        }

        [Fact]
        public async Task Missing_public_asset_is_a_plain_404()
        {
            var (status, _) = await GetAsync("/uploads/avatars/does-not-exist.png");

            Assert.Equal(StatusCodes.Status404NotFound, status);
        }
    }

    // How a STORED attachment value maps to a blob -- the validation that guards both reading and
    // deleting. Nothing outside the two private folders may ever come out of it.
    public class PrivateAttachmentRulesTests
    {
        [Theory]
        [InlineData("/uploads/chat-attachments/790f4671-ad63-4cbe-a644-784aeceaafec.png", "chat-attachments", "chat-attachments/790f4671-ad63-4cbe-a644-784aeceaafec.png")]
        [InlineData("/uploads/dm-attachments/7e242e4a-68fb-44fb-9e50-e0bef7c3e698.webm", "dm-attachments", "dm-attachments/7e242e4a-68fb-44fb-9e50-e0bef7c3e698.webm")]
        [InlineData("/uploads/chat-attachments/790f4671-ad63-4cbe-a644-784aeceaafec.PDF", "chat-attachments", "chat-attachments/790f4671-ad63-4cbe-a644-784aeceaafec.PDF")]
        [InlineData("https://api.scoram.in/uploads/dm-attachments/7e242e4a-68fb-44fb-9e50-e0bef7c3e698.png?x=1", "dm-attachments", "dm-attachments/7e242e4a-68fb-44fb-9e50-e0bef7c3e698.png")]
        [InlineData("  /uploads/chat-attachments/a1.png#frag ", "chat-attachments", "chat-attachments/a1.png")]
        public void Valid_stored_values_map_to_their_blob(string stored, string folder, string expectedBlob)
        {
            Assert.True(PrivateAttachmentRules.TryGetBlobName(stored, folder, out var blobName));
            Assert.Equal(expectedBlob, blobName);
        }

        [Theory]
        [InlineData(null, "chat-attachments")]
        [InlineData("", "chat-attachments")]
        [InlineData("   ", "chat-attachments")]
        [InlineData("/uploads/avatars/a.png", "chat-attachments")]                       // a PUBLIC asset is never a private attachment
        [InlineData("/uploads/question-images/a.png", "dm-attachments")]
        [InlineData("/uploads/dm-attachments/a.png", "chat-attachments")]                // wrong private folder for this message type
        [InlineData("/uploads/chat-attachments/../avatars/a.png", "chat-attachments")]   // traversal
        [InlineData("/uploads/chat-attachments/sub/a.png", "chat-attachments")]          // nested
        [InlineData("/uploads/chat-attachments/a%2e%2e.png", "chat-attachments")]        // encoded characters
        [InlineData("/uploads/chat-attachments/a\\b.png", "chat-attachments")]           // backslash
        [InlineData("/uploads/chat-attachments/run.exe", "chat-attachments")]            // extension we never store
        [InlineData("/uploads/chat-attachments/noextension", "chat-attachments")]
        [InlineData("/uploads/chat-attachments/", "chat-attachments")]
        [InlineData("uploads/chat-attachments/a.png", "chat-attachments")]               // not rooted
        [InlineData("/uploads/chat-attachments/a.png", "avatars")]                       // required folder is not private
        [InlineData("/uploads/chat-attachments/a.png", "..")]
        public void Anything_else_is_rejected(string? stored, string folder)
        {
            Assert.False(PrivateAttachmentRules.TryGetBlobName(stored, folder, out var blobName));
            Assert.Equal(string.Empty, blobName);
        }

        [Fact]
        public void Public_and_private_folder_lists_never_overlap()
        {
            Assert.DoesNotContain(PrivateAttachmentRules.ChatFolder, PrivateAttachmentRules.PublicFolders, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(PrivateAttachmentRules.DirectMessageFolder, PrivateAttachmentRules.PublicFolders, StringComparer.OrdinalIgnoreCase);
            Assert.True(PrivateAttachmentRules.IsPrivateFolder("chat-attachments"));
            Assert.True(PrivateAttachmentRules.IsPrivateFolder("DM-Attachments"));
            Assert.False(PrivateAttachmentRules.IsPrivateFolder("avatars"));
            Assert.False(PrivateAttachmentRules.IsPublicFolder("chat-attachments"));
            Assert.False(PrivateAttachmentRules.IsPublicFolder(null));
        }

        [Fact]
        public void Attachment_paths_expose_only_the_message_id()
        {
            var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
            Assert.Equal("/api/chat/messages/11111111-1111-1111-1111-111111111111/attachment", PrivateAttachmentRules.ChatAttachmentPath(id));
            Assert.Equal("/api/directmessages/messages/11111111-1111-1111-1111-111111111111/attachment", PrivateAttachmentRules.DirectMessageAttachmentPath(id));
        }
    }
}
