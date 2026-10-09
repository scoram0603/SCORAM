using ScoramAPI.DTOs;
using ScoramAPI.Services;
using Xunit;

namespace ScoramAPI.Tests.Services
{
    public class StimulusContentValidatorTests
    {
        private static ContentBlockDto B(string type, string content) => new() { Type = type, Content = content };

        [Fact]
        public void Valid_TextImageTable_Serializes()
        {
            var json = StimulusContentValidator.ValidateAndSerialize(new()
            {
                B("text", "Read the passage"), B("image", "/uploads/stimulus-images/a.png"), B("table", "[[\"A\",\"B\"],[\"1\",\"2\"]]")
            }, requireContent: true);
            Assert.NotNull(json);
            Assert.Equal(3, ContentBlocksJsonHelper.Parse(json).Count);
        }

        [Fact]
        public void Empty_IsRejectedWhenRequired_NullWhenOptional()
        {
            Assert.Throws<ArgumentException>(() => StimulusContentValidator.ValidateAndSerialize(new(), true));
            Assert.Null(StimulusContentValidator.ValidateAndSerialize(null, false));
        }

        [Theory]
        [InlineData("https://evil.com/a.png")]
        [InlineData("//evil.com/a.png")]
        [InlineData("javascript:alert(1)")]
        [InlineData("/uploads/../secrets.png")]
        [InlineData("/other/a.png")]
        [InlineData("C:\\temp\\a.png")]
        [InlineData("/uploads/a.png?x=1")]
        public void UnsafeImageUrls_Rejected(string url) =>
            Assert.Throws<ArgumentException>(() => StimulusContentValidator.ValidateAndSerialize(new() { B("image", url) }, true));

        [Fact]
        public void BadTableJson_UnknownType_EmptyContent_TooLong_AreRejected()
        {
            Assert.Throws<ArgumentException>(() => StimulusContentValidator.ValidateAndSerialize(new() { B("table", "not json") }, true));
            Assert.Throws<ArgumentException>(() => StimulusContentValidator.ValidateAndSerialize(new() { B("video", "x") }, true));
            Assert.Throws<ArgumentException>(() => StimulusContentValidator.ValidateAndSerialize(new() { B("text", " ") }, true));
            Assert.Throws<ArgumentException>(() => StimulusContentValidator.ValidateAndSerialize(new() { B("text", new string('a', StimulusContentValidator.MaxBlockLength + 1)) }, true));
        }
    }
}
