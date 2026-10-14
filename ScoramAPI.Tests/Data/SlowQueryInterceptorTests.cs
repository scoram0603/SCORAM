using ScoramAPI.Data;
using Xunit;

namespace ScoramAPI.Tests.Data
{
    public class SlowQueryInterceptorTests
    {
        [Fact]
        public void IsSlow_IsInclusiveOfTheThreshold()
        {
            Assert.True(SlowQueryInterceptor.IsSlow(TimeSpan.FromMilliseconds(500), 500));
            Assert.False(SlowQueryInterceptor.IsSlow(TimeSpan.FromMilliseconds(499.9), 500));
        }

        [Fact]
        public void Summarise_CollapsesWhitespace_AndTruncatesLongSql()
        {
            var sql = "SELECT  [a].[Id]\r\n  FROM   [T] AS [a]\n WHERE [a].[X] = @p0";
            Assert.Equal("SELECT [a].[Id] FROM [T] AS [a] WHERE [a].[X] = @p0", SlowQueryInterceptor.Summarise(sql));

            var long300 = "SELECT " + new string('x', 1000);
            var summary = SlowQueryInterceptor.Summarise(long300);
            Assert.EndsWith("...", summary);
            Assert.Equal(303, summary.Length);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Summarise_HandlesEmptyInput(string? sql) => Assert.Equal(string.Empty, SlowQueryInterceptor.Summarise(sql));
    }
}
