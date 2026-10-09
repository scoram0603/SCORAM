using ScoramAPI.Models;
using ScoramAPI.Services;
using Xunit;

namespace ScoramAPI.Tests.Services
{
    public class PaperInstructionOrderingTests
    {
        private static List<PaperInstruction> Make(int n) =>
            Enumerable.Range(0, n).Select(i => new PaperInstruction { DisplayOrder = i }).ToList();

        [Fact]
        public void Apply_ReordersAllActive()
        {
            var a = Make(3);
            PaperInstructionOrdering.Apply(a, new[] { a[2].Id, a[0].Id, a[1].Id });
            Assert.Equal(0, a[2].DisplayOrder); Assert.Equal(1, a[0].DisplayOrder); Assert.Equal(2, a[1].DisplayOrder);
        }

        [Fact]
        public void Apply_RejectsMissingExtraDuplicateOrUnknown()
        {
            var a = Make(3);
            Assert.Throws<ArgumentException>(() => PaperInstructionOrdering.Apply(a, new[] { a[0].Id, a[1].Id }));
            Assert.Throws<ArgumentException>(() => PaperInstructionOrdering.Apply(a, new[] { a[0].Id, a[1].Id, a[2].Id, Guid.NewGuid() }));
            Assert.Throws<ArgumentException>(() => PaperInstructionOrdering.Apply(a, new[] { a[0].Id, a[0].Id, a[1].Id }));
            Assert.Throws<ArgumentException>(() => PaperInstructionOrdering.Apply(a, new[] { a[0].Id, a[1].Id, Guid.NewGuid() }));
            Assert.Equal(new[] { 0, 1, 2 }, a.Select(x => x.DisplayOrder).ToArray()); // nothing changed on failure
        }
    }
}
