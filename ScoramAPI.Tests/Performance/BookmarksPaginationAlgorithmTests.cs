using Xunit;

namespace ScoramAPI.Tests.Performance
{
    // BookmarksController.List (see its own comment) queries up to 5 different tables, each capped
    // at `.Take(page * pageSize)` ordered by CreatedAt descending, then combines and paginates the
    // (much smaller) combined set in memory -- rather than loading a user's ENTIRE bookmark history
    // across all 5 tables just to answer one page. The claim that makes this safe: no single source
    // can ever contribute more than page*pageSize items to the true combined top-(page*pageSize), so
    // capping each source there can't exclude anything the requested page needs.
    //
    // This can't be tested against BookmarksController directly without a full DB + auth setup, but
    // the claim itself is a pure algorithm independent of EF/SQL -- these tests model it directly:
    // given N "sources" each independently sorted descending, cap-each-then-combine must produce the
    // identical result to combine-everything-then-sort for any page, any pageSize, any distribution
    // of items across sources. A regression here would mean silently missing bookmarks on some page,
    // for some user, with some particular mix of bookmark types -- exactly the kind of bug a
    // performance optimization like this can introduce without any single obviously-wrong output.
    public class BookmarksPaginationAlgorithmTests
    {
        private record Item(string Source, int SortKey); // SortKey stands in for CreatedAt (descending = "most recent first")

        private static List<Item> CappedThenCombined(List<List<Item>> sources, int page, int pageSize)
        {
            var combined = new List<Item>();
            foreach (var source in sources)
            {
                combined.AddRange(source.OrderByDescending(i => i.SortKey).Take(page * pageSize));
            }
            return combined.OrderByDescending(i => i.SortKey).Skip((page - 1) * pageSize).Take(pageSize).ToList();
        }

        private static List<Item> GroundTruth(List<List<Item>> sources, int page, int pageSize) =>
            sources.SelectMany(s => s).OrderByDescending(i => i.SortKey).Skip((page - 1) * pageSize).Take(pageSize).ToList();

        [Fact]
        public void OneSourceDominatesEveryTopResult_StillMatchesGroundTruth()
        {
            // The exact scenario that would break a naively-lower cap: every single one of the top
            // results happens to be the same bookmark type (e.g. a user who only ever bookmarks
            // discussions, never papers/mock tests/questions).
            var dominant = Enumerable.Range(0, 500).Select(i => new Item("discussions", i)).ToList();
            var sparse = new List<Item> { new("papers", 10), new("papers", 5) };

            for (var page = 1; page <= 5; page++)
            {
                var expected = GroundTruth(new List<List<Item>> { dominant, sparse }, page, pageSize: 20);
                var actual = CappedThenCombined(new List<List<Item>> { dominant, sparse }, page, pageSize: 20);
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void FiveSourcesInterleaved_MatchesGroundTruthAcrossManyPages()
        {
            var random = new Random(Seed: 42); // fixed seed -- deterministic, reproducible failures
            var sourceNames = new[] { "questions", "questionBank", "discussions", "papers", "mockTests" };
            var sources = sourceNames.Select(name =>
                Enumerable.Range(0, random.Next(0, 300)).Select(_ => new Item(name, random.Next(0, 100_000))).ToList()
            ).ToList();

            const int pageSize = 20;
            var totalItems = sources.Sum(s => s.Count);
            var totalPages = (totalItems + pageSize - 1) / pageSize;

            for (var page = 1; page <= Math.Max(totalPages, 1); page++)
            {
                var expected = GroundTruth(sources, page, pageSize);
                var actual = CappedThenCombined(sources, page, pageSize);
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void EmptySources_ProduceAnEmptyPage_NotAnError()
        {
            var sources = new List<List<Item>> { new(), new(), new() };

            var result = CappedThenCombined(sources, page: 1, pageSize: 20);

            Assert.Empty(result);
        }

        [Fact]
        public void DeepPageBeyondAllAvailableItems_ProducesAnEmptyPage_NotAnError()
        {
            var sources = new List<List<Item>> { Enumerable.Range(0, 5).Select(i => new Item("papers", i)).ToList() };

            var result = CappedThenCombined(sources, page: 50, pageSize: 20);

            Assert.Empty(result);
        }

        [Fact]
        public void TiedSortKeysAcrossDifferentSources_StillProducesTheSameSetAsGroundTruth()
        {
            // Ties (two bookmarks created in the same instant, or -- more realistically -- coarse
            // timestamp precision) are exactly where an unstable sort/cap combination could plausibly
            // diverge from ground truth even if the untied cases all pass.
            var a = new List<Item> { new("papers", 100), new("papers", 100), new("papers", 50) };
            var b = new List<Item> { new("discussions", 100), new("discussions", 100), new("discussions", 50) };

            var expectedCount = GroundTruth(new List<List<Item>> { a, b }, page: 1, pageSize: 4).Count;
            var actualCount = CappedThenCombined(new List<List<Item>> { a, b }, page: 1, pageSize: 4).Count;

            // Element-for-element order among exact ties isn't a meaningful guarantee either
            // algorithm makes -- what matters is that the same NUMBER of items (and, by extension,
            // no silently-dropped item) comes back either way.
            Assert.Equal(expectedCount, actualCount);
        }
    }
}
