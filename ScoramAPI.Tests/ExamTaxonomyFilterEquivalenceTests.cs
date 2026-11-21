// Written without a .NET SDK available; not yet executed.
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ScoramAPI.Data;
using ScoramAPI.Models;
using ScoramAPI.Services;
using System.Text.RegularExpressions;
using Xunit;

namespace ScoramAPI.Tests.Services
{
    // PERFORMANCE (audit P2, round 5): ExamTaxonomyService.FilterByExamSubjects / FilterByExamTopics were
    // rewritten from "OR of nested correlated EXISTS" to "x.Id IN (explicit UNION fallback)".
    // They had NO tests before. These prove the rewrite returns exactly the same questions:
    //   * RefSubjects / RefTopics below are the PREVIOUS predicates, copied verbatim (the oracle);
    //   * every id-list scenario runs both against the same seeded database and requires identical ids + counts;
    //   * one scenario is also checked against a hand-computed expectation, so the oracle itself is pinned.
    // The seed deliberately contains every edge the old predicate encoded: stale (unmapped) classification rows,
    // a NULL-subject tombstone that blocks the fallback, subjects/topics without a base, one question in two
    // exams / two years (no duplicates), an inactive question, an unmapped question, an id list spanning exams.
    //
    // Limits (honest): the in-memory provider is not SQL Server -- it proves the RESULTS. The last test builds
    // the SQL offline (no connection) and pins its SHAPE; it still says nothing about the plan or timing.
    public class ExamTaxonomyFilterEquivalenceTests : IDisposable
    {
        private readonly string _dbName = Guid.NewGuid().ToString();

        private readonly Exam _x = new() { Name = "X" };
        private readonly Exam _y = new() { Name = "Y" };
        private readonly QuestionBankSubject _g1 = new() { Name = "G1" };
        private readonly QuestionBankSubject _g2 = new() { Name = "G2" };
        private readonly QuestionBankTopic _gt1, _gt2, _gt3;

        // exam taxonomy
        private readonly ExamSubject _xS1, _xS2, _xS3NoBase, _yS1;
        private readonly ExamTopic _xT1, _xT2, _xT3, _xT4NoBase, _yT1;

        private readonly Dictionary<string, Guid> _q = new();

        public ExamTaxonomyFilterEquivalenceTests()
        {
            _gt1 = new() { Name = "GT1", SubjectId = _g1.Id };
            _gt2 = new() { Name = "GT2", SubjectId = _g1.Id };
            _gt3 = new() { Name = "GT3", SubjectId = _g2.Id };

            _xS1 = new() { ExamId = _x.Id, Name = "X-S1", BaseSubjectId = _g1.Id };
            _xS2 = new() { ExamId = _x.Id, Name = "X-S2", BaseSubjectId = _g2.Id };
            _xS3NoBase = new() { ExamId = _x.Id, Name = "X-S3" };
            _yS1 = new() { ExamId = _y.Id, Name = "Y-S1", BaseSubjectId = _g1.Id };
            _xT1 = new() { ExamSubjectId = _xS1.Id, Name = "X-T1", BaseTopicId = _gt1.Id };
            _xT2 = new() { ExamSubjectId = _xS1.Id, Name = "X-T2", BaseTopicId = _gt2.Id };
            _xT3 = new() { ExamSubjectId = _xS2.Id, Name = "X-T3", BaseTopicId = _gt3.Id };
            _xT4NoBase = new() { ExamSubjectId = _xS3NoBase.Id, Name = "X-T4" };
            _yT1 = new() { ExamSubjectId = _yS1.Id, Name = "Y-T1", BaseTopicId = _gt1.Id };
            Seed();
        }

        public void Dispose()
        {
            using var db = NewContext();
            db.Database.EnsureDeleted();
        }

        private ScoramDbContext NewContext() => new(
            new DbContextOptionsBuilder<ScoramDbContext>().UseInMemoryDatabase(_dbName).Options);

        private ExamTaxonomyService NewService(ScoramDbContext db) =>
            new(db, Mock.Of<ILogger<ExamTaxonomyService>>());

        private QuestionBankQuestion Question(ScoramDbContext db, string name, QuestionBankSubject s, QuestionBankTopic t,
            bool active = true)
        {
            var q = new QuestionBankQuestion { QuestionText = name, SubjectId = s.Id, TopicId = t.Id, IsActive = active };
            db.QuestionBankQuestions.Add(q);
            _q[name] = q.Id;
            return q;
        }

        private static void Map(ScoramDbContext db, QuestionBankQuestion q, Exam e, int year) =>
            db.QuestionBankExamMappings.Add(new QuestionBankExamMapping { QuestionBankQuestionId = q.Id, ExamId = e.Id, Year = year });

        private static void Classify(ScoramDbContext db, QuestionBankQuestion q, Exam e, ExamSubject? s, ExamTopic? t) =>
            db.QuestionBankExamClassifications.Add(new QuestionBankExamClassification
            { QuestionBankQuestionId = q.Id, ExamId = e.Id, ExamSubjectId = s?.Id, ExamTopicId = t?.Id });

        private void Seed()
        {
            using var db = NewContext();
            db.AddRange(_x, _y, _g1, _g2, _gt1, _gt2, _gt3, _xS1, _xS2, _xS3NoBase, _yS1, _xT1, _xT2, _xT3, _xT4NoBase, _yT1);

            // q1: G1/GT1 mapped to X, no classification            -> fallback X-S1 / X-T1
            var q1 = Question(db, "q1", _g1, _gt1); Map(db, q1, _x, 2020);
            // q2: G1/GT2 mapped to X AND Y (two years)             -> fallback X-S1 / X-T2; for Y only the subject matches
            var q2 = Question(db, "q2", _g1, _gt2); Map(db, q2, _x, 2020); Map(db, q2, _x, 2021); Map(db, q2, _y, 2019);
            // q3: G2/GT3 mapped to X, explicit row -> X-S3/X-T4    -> the row blocks the X-S2 / X-T3 fallback
            var q3 = Question(db, "q3", _g2, _gt3); Map(db, q3, _x, 2020); Classify(db, q3, _x, _xS3NoBase, _xT4NoBase);
            // q4: G1/GT1 mapped to X, NULL/NULL tombstone row      -> blocks every fallback, matches nothing
            var q4 = Question(db, "q4", _g1, _gt1); Map(db, q4, _x, 2020); Classify(db, q4, _x, null, null);
            // q5: G1/GT1 mapped to Y only; STALE explicit row for X-S1 (not mapped to X) -> must not match X-S1 / X-T1; matches Y via fallback
            var q5 = Question(db, "q5", _g1, _gt1); Map(db, q5, _y, 2020); Classify(db, q5, _x, _xS1, _xT1);
            // q6: G1/GT1 mapped to X only; stale row for exam Y (not mapped to Y)  -> no row for (q6, X): X fallback applies; Y must not match
            var q6 = Question(db, "q6", _g1, _gt1); Map(db, q6, _x, 2020); Classify(db, q6, _y, _yS1, _yT1);
            // q7: inactive question, G1/GT1 mapped to X: the filter itself never looked at IsActive
            var q7 = Question(db, "q7", _g1, _gt1, active: false); Map(db, q7, _x, 2020);
            // q8: G1/GT1 mapped to X in three years              -> exactly one row
            var q8 = Question(db, "q8", _g1, _gt1); Map(db, q8, _x, 2018); Map(db, q8, _x, 2019); Map(db, q8, _x, 2020);
            // q9: G2/GT3 mapped to X with an explicit row to X-S2/X-T3
            var q9 = Question(db, "q9", _g2, _gt3); Map(db, q9, _x, 2020); Classify(db, q9, _x, _xS2, _xT3);
            // q10: G1/GT1 not mapped to any exam
            Question(db, "q10", _g1, _gt1);
            // q11: G1/GT2 mapped to X with an explicit row to X-S1 but NO topic (topic NULL): subject explicit, topic tombstone
            var q11 = Question(db, "q11", _g1, _gt2); Map(db, q11, _x, 2020); Classify(db, q11, _x, _xS1, null);
            db.SaveChanges();
        }

        // ---- the previous implementations, verbatim (the oracle) ---------------------------------------

        private static IQueryable<QuestionBankQuestion> RefSubjects(ScoramDbContext _db, IQueryable<QuestionBankQuestion> source, List<Guid> list) =>
            source.Where(x =>
                _db.QuestionBankExamClassifications.Any(c => c.QuestionBankQuestionId == x.Id && c.ExamSubjectId != null && list.Contains(c.ExamSubjectId.Value)
                    && _db.QuestionBankExamMappings.Any(m => m.QuestionBankQuestionId == x.Id && m.ExamId == c.ExamId))
                || _db.ExamSubjects.Any(es => list.Contains(es.Id) && es.BaseSubjectId == x.SubjectId
                    && _db.QuestionBankExamMappings.Any(m => m.QuestionBankQuestionId == x.Id && m.ExamId == es.ExamId)
                    && !_db.QuestionBankExamClassifications.Any(c => c.QuestionBankQuestionId == x.Id && c.ExamId == es.ExamId)));

        private static IQueryable<QuestionBankQuestion> RefTopics(ScoramDbContext _db, IQueryable<QuestionBankQuestion> source, List<Guid> list) =>
            source.Where(x =>
                _db.QuestionBankExamClassifications.Any(c => c.QuestionBankQuestionId == x.Id && c.ExamTopicId != null && list.Contains(c.ExamTopicId.Value)
                    && _db.QuestionBankExamMappings.Any(m => m.QuestionBankQuestionId == x.Id && m.ExamId == c.ExamId))
                || _db.ExamTopics.Any(et => list.Contains(et.Id) && et.BaseTopicId == x.TopicId
                    && _db.ExamSubjects.Any(es => es.Id == et.ExamSubjectId && es.BaseSubjectId == x.SubjectId
                        && _db.QuestionBankExamMappings.Any(m => m.QuestionBankQuestionId == x.Id && m.ExamId == es.ExamId)
                        && !_db.QuestionBankExamClassifications.Any(c => c.QuestionBankQuestionId == x.Id && c.ExamId == es.ExamId))));

        private string Names(IEnumerable<Guid> ids) =>
            string.Join(",", ids.Select(id => _q.First(kv => kv.Value == id).Key).OrderBy(n => n, StringComparer.Ordinal));

        // ---- equivalence -------------------------------------------------------------------------------

        private IEnumerable<(string Name, Func<List<Guid>> Ids)> SubjectScenarios() => new (string, Func<List<Guid>>)[]
        {
            ("X-S1", () => new List<Guid> { _xS1.Id }),
            ("X-S2", () => new List<Guid> { _xS2.Id }),
            ("X-S3 (no base)", () => new List<Guid> { _xS3NoBase.Id }),
            ("Y-S1", () => new List<Guid> { _yS1.Id }),
            ("X-S1 + Y-S1 (spans exams)", () => new List<Guid> { _xS1.Id, _yS1.Id }),
            ("X-S1 + X-S2", () => new List<Guid> { _xS1.Id, _xS2.Id }),
            ("all four", () => new List<Guid> { _xS1.Id, _xS2.Id, _xS3NoBase.Id, _yS1.Id }),
            ("empty list", () => new List<Guid>()),
            ("unknown id", () => new List<Guid> { Guid.NewGuid() }),
            ("duplicate ids", () => new List<Guid> { _xS1.Id, _xS1.Id }),
        };

        private IEnumerable<(string Name, Func<List<Guid>> Ids)> TopicScenarios() => new (string, Func<List<Guid>>)[]
        {
            ("X-T1", () => new List<Guid> { _xT1.Id }),
            ("X-T2", () => new List<Guid> { _xT2.Id }),
            ("X-T3", () => new List<Guid> { _xT3.Id }),
            ("X-T4 (no base)", () => new List<Guid> { _xT4NoBase.Id }),
            ("Y-T1", () => new List<Guid> { _yT1.Id }),
            ("X-T1 + X-T3", () => new List<Guid> { _xT1.Id, _xT3.Id }),
            ("X-T2 + Y-T1 (spans exams)", () => new List<Guid> { _xT2.Id, _yT1.Id }),
            ("all five", () => new List<Guid> { _xT1.Id, _xT2.Id, _xT3.Id, _xT4NoBase.Id, _yT1.Id }),
            ("empty list", () => new List<Guid>()),
            ("unknown id", () => new List<Guid> { Guid.NewGuid() }),
        };

        [Fact]
        public async Task FilterByExamSubjects_ReturnsExactlyWhatThePreviousPredicateReturned()
        {
            using var db = NewContext();
            var svc = NewService(db);
            foreach (var (name, ids) in SubjectScenarios())
            {
                var list = ids();
                var expected = await RefSubjects(db, db.QuestionBankQuestions, list).Select(q => q.Id).ToListAsync();
                var actual = await svc.FilterByExamSubjects(db.QuestionBankQuestions, list).Select(q => q.Id).ToListAsync();
                Assert.True(Names(expected) == Names(actual), $"subjects scenario '{name}': expected [{Names(expected)}] but got [{Names(actual)}]");
                Assert.Equal(expected.Count, actual.Count);   // no duplicates introduced
                Assert.Equal(actual.Count, actual.Distinct().Count());
                Assert.Equal(expected.Count, await svc.FilterByExamSubjects(db.QuestionBankQuestions, list).CountAsync());
            }
        }

        [Fact]
        public async Task FilterByExamTopics_ReturnsExactlyWhatThePreviousPredicateReturned()
        {
            using var db = NewContext();
            var svc = NewService(db);
            foreach (var (name, ids) in TopicScenarios())
            {
                var list = ids();
                var expected = await RefTopics(db, db.QuestionBankQuestions, list).Select(q => q.Id).ToListAsync();
                var actual = await svc.FilterByExamTopics(db.QuestionBankQuestions, list).Select(q => q.Id).ToListAsync();
                Assert.True(Names(expected) == Names(actual), $"topics scenario '{name}': expected [{Names(expected)}] but got [{Names(actual)}]");
                Assert.Equal(actual.Count, actual.Distinct().Count());
                Assert.Equal(expected.Count, await svc.FilterByExamTopics(db.QuestionBankQuestions, list).CountAsync());
            }
        }

        // The oracle must not be vacuous: pin a few hand-computed answers (so a bug shared by the oracle and
        // the new code, or an empty-vs-empty comparison, cannot pass).
        [Fact]
        public async Task HandComputedExpectations_PinTheSemantics()
        {
            using var db = NewContext();
            var svc = NewService(db);
            async Task<string> Subj(params Guid[] ids) => Names(await svc.FilterByExamSubjects(db.QuestionBankQuestions, ids).Select(q => q.Id).ToListAsync());
            async Task<string> Topic(params Guid[] ids) => Names(await svc.FilterByExamTopics(db.QuestionBankQuestions, ids).Select(q => q.Id).ToListAsync());

            // X-S1 (base G1, exam X): fallback = G1 questions mapped to X with no (question, X) row -> q1 q2 q6 q7 q8;
            // explicit = rows naming X-S1 AND still mapped to X -> q11 (q5's row is stale: q5 is not mapped to X);
            // q4 (tombstone) and q10 (unmapped) never match; q3/q9 are G2.
            Assert.Equal("q1,q11,q2,q6,q7,q8", await Subj(_xS1.Id));
            // X-S2 (base G2): q9 matches by its explicit row; q3 is a G2 question mapped to X but has a row for exam X
            // pointing at X-S3, which blocks the fallback. So only q9.
            Assert.Equal("q9", await Subj(_xS2.Id));
            // X-S3 has no base: explicit rows only -> q3.
            Assert.Equal("q3", await Subj(_xS3NoBase.Id));
            // Y-S1 (base G1, exam Y): fallback = G1 questions mapped to Y with no (question, Y) row -> q2, q5; q6's Y row is stale.
            Assert.Equal("q2,q5", await Subj(_yS1.Id));
            // nothing asked, nothing returned
            Assert.Equal("", await Subj());

            // X-T1 (base GT1, subject X-S1): GT1 questions of G1 mapped to X, no X row -> q1 q6 q7 q8; explicit rows naming X-T1 and mapped to X: none (q5 stale)
            Assert.Equal("q1,q6,q7,q8", await Topic(_xT1.Id));
            // X-T2 (base GT2): q2 by fallback; q11 has a row (topic NULL) which blocks the fallback and names no topic -> not matched
            Assert.Equal("q2", await Topic(_xT2.Id));
            Assert.Equal("q9", await Topic(_xT3.Id));
            Assert.Equal("q3", await Topic(_xT4NoBase.Id));
            Assert.Equal("q2,q5", await Topic(_xT2.Id, _yT1.Id));
        }

        [Fact]
        public async Task Filters_ComposeWithAnyOtherPredicate_AndKeepOrdering()
        {
            using var db = NewContext();
            var svc = NewService(db);
            // composed the way QuestionBankController does: IsActive first, filter, then order -- q7 (inactive) drops out
            var ids = await svc.FilterByExamSubjects(db.QuestionBankQuestions.Where(q => q.IsActive), new[] { _xS1.Id })
                .OrderBy(q => q.QuestionText).Select(q => q.QuestionText).ToListAsync();
            Assert.Equal(new[] { "q1", "q11", "q2", "q6", "q8" }, ids);
        }

        // The in-memory provider cannot show that the new LINQ translates to SQL Server at all, nor its shape.
        // EF can build the SQL without a connection, so pin: it translates, it is a UNION semi-join, and there is
        // no OR between EXISTS predicates any more (the old shape). Never executed against a database.
        [Fact]
        public void GeneratedSql_IsAUnionSemiJoin_NotAnOrOfExists()
        {
            var options = new DbContextOptionsBuilder<ScoramDbContext>()
                .UseSqlServer("Server=.;Database=ScoramOfflineSqlShapeOnly;Trusted_Connection=True;TrustServerCertificate=True")
                .Options;
            using var db = new ScoramDbContext(options);
            var svc = NewService(db);

            var subjectSql = svc.FilterByExamSubjects(db.QuestionBankQuestions, new[] { Guid.NewGuid(), Guid.NewGuid() }).ToQueryString();
            var topicSql = svc.FilterByExamTopics(db.QuestionBankQuestions, new[] { Guid.NewGuid() }).ToQueryString();

            foreach (var sql in new[] { subjectSql, topicSql })
            {
                Assert.Contains("UNION", sql, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotMatch(new Regex(@"\bOR\s+(NOT\s+)?EXISTS", RegexOptions.IgnoreCase), sql);
            }
        }
    }
}
