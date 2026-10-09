using ScoramAPI.Models;
using ScoramAPI.Services;
using Xunit;

namespace ScoramAPI.Tests.Services
{
    public class SharedStimulusBusinessIdTests
    {
        [Theory]
        [InlineData(1, "STM0001")]
        [InlineData(42, "STM0042")]
        [InlineData(9999, "STM9999")]
        [InlineData(10000, "STM10000")]
        public void Format_PadsToFourDigitsAndGrows(int n, string expected) =>
            Assert.Equal(expected, BusinessIdFormats.Format(BusinessIdEntityType.SharedStimulus, n));

        [Theory]
        [InlineData("STM0001", true)]
        [InlineData("stm0007", true)]
        [InlineData("STM001", false)]
        [InlineData("STM0000", false)]
        [InlineData("TST0001", false)]
        [InlineData("STM00A1", false)]
        public void IsValid_FollowsFormat(string id, bool ok) =>
            Assert.Equal(ok, BusinessIdFormats.IsValid(BusinessIdEntityType.SharedStimulus, id));

        [Fact]
        public void CounterKey_IsPrefix() =>
            Assert.Equal("STM", BusinessIdFormats.CounterKeyFor(BusinessIdEntityType.SharedStimulus));

        [Theory]
        [InlineData("shared-stimulus")]
        [InlineData("stimuli")]
        [InlineData("Shared Stimuli")]
        public void TryParseEntityType_AcceptsAliases(string raw)
        {
            Assert.True(BusinessIdFormats.TryParseEntityType(raw, out var t));
            Assert.Equal(BusinessIdEntityType.SharedStimulus, t);
        }

        [Fact]
        public void ExistingTypes_Unchanged()
        {
            Assert.Equal("TST0001", BusinessIdFormats.Format(BusinessIdEntityType.Test, 1));
            Assert.Equal("SUB001", BusinessIdFormats.Format(BusinessIdEntityType.Subject, 1));
            Assert.Equal("ADM0001", BusinessIdFormats.Format(BusinessIdEntityType.Admin, 1));
        }

        [Fact]
        public void TypeOf_MapsSharedStimulus() =>
            Assert.Equal(BusinessIdEntityType.SharedStimulus, BusinessIdGenerator.TypeOf(new SharedStimulus()));
    }
}
