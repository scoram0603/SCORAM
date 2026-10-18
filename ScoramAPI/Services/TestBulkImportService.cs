using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    public class TestImportException : Exception
    {
        public int StatusCode { get; }
        public TestImportException(string message, int statusCode = 400) : base(message) { StatusCode = statusCode; }
    }

    public interface ITestBulkImportService
    {
        Task<TestImportPreviewDto> PreviewAsync(Stream file, string fileName, ImportFileFormat format, TestImportKind kind, Guid adminId);
        Task<TestImportCommitResultDto> CommitAsync(string token, IReadOnlyCollection<int>? selectedIndexes, Guid adminId);
        Task<TestImportRollbackResultDto> RollbackAsync(Guid jobId, Guid adminId);
        Task<List<TestImportJobDto>> HistoryAsync(TestImportKind? kind);
        Task<TestImportJobDto?> GetJobAsync(Guid jobId);
    }

    /// <summary>
    /// Bulk upload of Mock Tests and Practice Test templates. Same lifecycle as the PYQ upload:
    /// Preview (parse + validate, nothing written except a PendingReview history row) -> Commit (one
    /// transaction) -> optional Rollback (removes exactly what the job created).
    ///
    /// Questions inside a test can be (1) a reference to an existing Question Bank question (by id, or by its
    /// text alone), (2) a full question that already exists in the Bank (matched by the same text+options
    /// identity the PYQ upload uses, so it is reused, never duplicated), or (3) a brand-new question, saved as
    /// a TEST-ONLY Question Bank question (IsTestOnly) -- it works in attempts, analytics, explanations and
    /// the Mistake Book, but never shows up in PYQ search/counts unless it is later uploaded as a real PYQ.
    /// </summary>
    public class TestBulkImportService : ITestBulkImportService
    {
        // Dates typed in a file are stored exactly as typed -- the same convention as the manual Create Mock Test form
        // (datetime-local value saved as-is, shown to students as-is). Set this to +05:30 only if that whole
        // convention is ever changed to real UTC.
        private static readonly TimeSpan TypedTimeOffset = TimeSpan.Zero;
        private static readonly TimeSpan StagedTtl = TimeSpan.FromMinutes(30);

        private readonly ScoramDbContext _db;
        private readonly IQuestionBankImportService _import;
        private readonly IStagedDataCache _cache;
        private readonly IAuditLogService _audit;

        public TestBulkImportService(ScoramDbContext db, IQuestionBankImportService import, IStagedDataCache cache, IAuditLogService audit)
        {
            _db = db;
            _import = import;
            _cache = cache;
            _audit = audit;
        }

        private static string CacheKey(string token) => $"testimport:{token}";

        // =============================================================================================
        // PREVIEW
        // =============================================================================================

        public async Task<TestImportPreviewDto> PreviewAsync(Stream file, string fileName, ImportFileFormat format, TestImportKind kind, Guid adminId)
        {
            var fileErrors = new List<string>();
            var tests = TestImportParser.Parse(file, format, kind, fileErrors);

            if (tests.Count == 0 && fileErrors.Count == 0) fileErrors.Add("No tests were found in the file.");
            if (tests.Count > TestImportParser.MaxTests)
            {
                fileErrors.Add($"A file can contain at most {TestImportParser.MaxTests} tests (found {tests.Count}). Split it into several files.");
                tests = new List<TestImportTestDto>();
            }
            if (tests.Sum(t => t.Questions.Count) > TestImportParser.MaxQuestions)
            {
                fileErrors.Add($"A file can contain at most {TestImportParser.MaxQuestions} questions in total. Split it into several files.");
                tests = new List<TestImportTestDto>();
            }

            if (tests.Count > 0) await ValidateAsync(tests, kind);

            // Abandoned previews older than a day are noise in the history list.
            var cutoff = DateTime.UtcNow.AddDays(-1);
            await _db.TestImportJobs.Where(j => j.Status == ImportJobStatus.PendingReview && j.CreatedAt < cutoff).ExecuteDeleteAsync();

            var job = new TestImportJob
            {
                CreatedByAdminId = adminId,
                FileName = fileName.Length > 255 ? fileName[..255] : fileName,
                Kind = kind,
                Format = format,
                Status = ImportJobStatus.PendingReview,
                TotalTests = tests.Count,
                ValidTests = tests.Count(t => t.IsValid),
                InvalidTests = tests.Count(t => !t.IsValid)
            };
            _db.TestImportJobs.Add(job);
            await _db.SaveChangesAsync();

            var token = Guid.NewGuid().ToString("N");
            await _cache.SetAsync(CacheKey(token), new TestImportStaged
            {
                JobId = job.Id,
                AdminId = adminId,
                Kind = kind.ToString(),
                Tests = tests
            }, StagedTtl);

            var dto = new TestImportPreviewDto
            {
                Token = token,
                JobId = job.Id,
                Kind = kind.ToString(),
                FileName = job.FileName,
                TotalTests = tests.Count,
                ValidTests = job.ValidTests,
                InvalidTests = job.InvalidTests,
                FileErrors = fileErrors,
                Tests = tests.Select(t => new TestImportPreviewTestDto
                {
                    Index = t.Index,
                    Title = t.Title,
                    IsValid = t.IsValid,
                    Errors = t.Errors,
                    Warnings = t.Warnings,
                    Exam = t.ExamName ?? t.Exam,
                    QuestionCount = t.Questions.Count,
                    NewQuestions = t.Questions.Count(q => q.Resolution == "New"),
                    ExistingQuestions = t.Questions.Count(q => q.Resolution == "Existing"),
                    TotalMinutes = t.TotalMinutes,
                    Status = t.Status,
                    ResultRelease = t.ResultRelease,
                    IsCurated = kind == TestImportKind.PracticeTemplate && t.Questions.Count > 0,
                    Sections = t.Sections.Select(s => new TestImportPreviewSectionDto
                    {
                        Name = s.Name,
                        DurationMinutes = s.DurationMinutes,
                        QuestionCount = t.Questions.Count(q => string.Equals(q.Section, s.Name, StringComparison.OrdinalIgnoreCase))
                    }).ToList()
                }).ToList()
            };
            dto.TotalNewQuestions = dto.Tests.Where(t => t.IsValid).Sum(t => t.NewQuestions);
            dto.TotalExistingQuestions = dto.Tests.Where(t => t.IsValid).Sum(t => t.ExistingQuestions);
            return dto;
        }

        // ---------------------------------------------------------------------------------------------
        // Validation
        // ---------------------------------------------------------------------------------------------

        private sealed record ExamInfo(Guid Id, string Name);
        private sealed record TopicInfo(Guid Id, Guid SubjectId, string Name);
        private sealed record BankText(Guid Id, string Key);

        private async Task ValidateAsync(List<TestImportTestDto> tests, TestImportKind kind)
        {
            var isMock = kind == TestImportKind.MockTest;

            var exams = (await _db.Exams.AsNoTracking().Select(e => new ExamInfo(e.Id, e.Name)).ToListAsync())
                .GroupBy(e => e.Name.Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
            var subjects = (await _db.QuestionBankSubjects.AsNoTracking().Where(s => s.IsActive).Select(s => new { s.Id, s.Name }).ToListAsync())
                .GroupBy(s => s.Name.Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First().Id);
            var topics = await _db.QuestionBankTopics.AsNoTracking().Where(t => t.IsActive)
                .Select(t => new TopicInfo(t.Id, t.SubjectId, t.Name)).ToListAsync();
            var existingTitles = (isMock
                    ? await _db.MockTests.AsNoTracking().Select(t => t.Title).ToListAsync()
                    : await _db.PracticeTestTemplates.AsNoTracking().Select(t => t.Title).ToListAsync())
                .Select(t => t.Trim().ToLowerInvariant()).ToHashSet();

            // ---- question lookups, once for the whole file ----
            var allQuestions = tests.SelectMany(t => t.Questions).ToList();
            bool HasText(TestImportQuestionDto q) => !string.IsNullOrWhiteSpace(q.QuestionText);
            bool AllOptions(TestImportQuestionDto q) =>
                !string.IsNullOrWhiteSpace(q.OptionA) && !string.IsNullOrWhiteSpace(q.OptionB)
                && !string.IsNullOrWhiteSpace(q.OptionC) && !string.IsNullOrWhiteSpace(q.OptionD);
            bool NoOptions(TestImportQuestionDto q) =>
                string.IsNullOrWhiteSpace(q.OptionA) && string.IsNullOrWhiteSpace(q.OptionB)
                && string.IsNullOrWhiteSpace(q.OptionC) && string.IsNullOrWhiteSpace(q.OptionD);

            var fullRows = allQuestions.Where(q => string.IsNullOrWhiteSpace(q.Ref) && HasText(q) && AllOptions(q))
                .Select(q => new QuestionBankImportRow { QuestionText = q.QuestionText, OptionA = q.OptionA, OptionB = q.OptionB, OptionC = q.OptionC, OptionD = q.OptionD })
                .ToList();
            var existingByKey = fullRows.Count == 0
                ? new Dictionary<string, ExistingQuestionRef>()
                : await _import.LoadExistingByDuplicateKeyAsync(_db, fullRows);

            var refIds = allQuestions.Where(q => !string.IsNullOrWhiteSpace(q.Ref) && Guid.TryParse(q.Ref, out _))
                .Select(q => Guid.Parse(q.Ref!)).Distinct().ToList();
            var correctById = new Dictionary<Guid, OptionLetter>();
            var lookupIds = refIds.Concat(existingByKey.Values.Select(v => v.Id)).Distinct().ToList();
            foreach (var chunk in lookupIds.Chunk(500))
            {
                var rows = await _db.QuestionBankQuestions.AsNoTracking()
                    .Where(q => q.IsActive && chunk.Contains(q.Id))
                    .Select(q => new { q.Id, q.CorrectOption }).ToListAsync();
                foreach (var r in rows) correctById[r.Id] = r.CorrectOption;
            }

            var textOnly = allQuestions.Where(q => string.IsNullOrWhiteSpace(q.Ref) && HasText(q) && NoOptions(q)).ToList();
            var textLookup = new Dictionary<string, List<BankText>>();
            foreach (var chunk in textOnly.Select(q => _import.NormalizeForDuplicateCheck(q.QuestionText)).Distinct().Chunk(500))
            {
                var rows = await _db.QuestionBankQuestions.AsNoTracking()
                    .Where(q => q.IsActive && chunk.Contains(q.NormalizedQuestionText))
                    .Select(q => new { q.Id, q.NormalizedQuestionText, q.QuestionText, q.OptionA, q.OptionB, q.OptionC, q.OptionD }).ToListAsync();
                foreach (var r in rows)
                {
                    if (!textLookup.TryGetValue(r.NormalizedQuestionText, out var list)) textLookup[r.NormalizedQuestionText] = list = new List<BankText>();
                    list.Add(new BankText(r.Id, _import.BuildDuplicateKey(r.QuestionText, r.OptionA, r.OptionB, r.OptionC, r.OptionD)));
                }
            }

            var titlesInFile = new Dictionary<string, int>();

            foreach (var t in tests)
            {
                var errors = t.Errors;
                var warnings = t.Warnings;
                t.Title = (t.Title ?? string.Empty).Trim();

                // ---- title ----
                if (t.Title.Length == 0) errors.Add("Title is required.");
                else
                {
                    if (t.Title.Length > 150) errors.Add("Title is too long (maximum 150 characters).");
                    var tk = t.Title.ToLowerInvariant();
                    if (titlesInFile.TryGetValue(tk, out var firstIdx)) errors.Add($"Another test in this file has the same title (test #{firstIdx + 1}).");
                    else titlesInFile[tk] = t.Index;
                    if (existingTitles.Contains(tk)) errors.Add("A test with this title already exists.");
                }

                // ---- exam ----
                if (!string.IsNullOrWhiteSpace(t.Exam))
                {
                    if (exams.TryGetValue(t.Exam.Trim().ToLowerInvariant(), out var exam)) { t.ExamId = exam.Id; t.ExamName = exam.Name; }
                    else errors.Add($"Exam \"{t.Exam}\" doesn't exist. Create it in Manage Exams first.");
                }
                else if (isMock) errors.Add("Exam is required.");

                // ---- negative marking / status / language ----
                if (string.IsNullOrWhiteSpace(t.NegativeMarking))
                {
                    t.NegativeMarkingValue = isMock ? 0.25m : 0m;
                    if (isMock) warnings.Add("NegativeMarking was blank -- using 0.25.");
                }
                else if (TryParseNegative(t.NegativeMarking, out var neg)) t.NegativeMarkingValue = neg;
                else errors.Add("NegativeMarking must be a number between 0 and 1 (for example 0.25 or 1/4).");

                if (string.IsNullOrWhiteSpace(t.Status)) t.Status = "Draft";
                else if (Enum.TryParse<TestPublishStatus>(t.Status.Trim(), true, out var st)) t.Status = st.ToString();
                else errors.Add("Status must be Draft, Published or Archived.");

                if (!string.IsNullOrWhiteSpace(t.Language) && !Enum.TryParse<PaperLanguage>(t.Language.Trim(), true, out _))
                    errors.Add("Language must be Hindi, English or Bilingual.");

                if (t.MaxAttempts is < 1) errors.Add("MaxAttempts must be at least 1.");

                // ---- questions ----
                ResolveQuestions(t, existingByKey, correctById, refIds, textLookup);

                if (isMock) ValidateMock(t);
                else ValidatePractice(t, subjects, topics);

                t.IsValid = errors.Count == 0;
            }

            // Practice pool-size warning (filter-based templates only) -- after the main loop so a failure
            // there can't hide the structural errors.
            if (!isMock)
            {
                foreach (var t in tests.Where(x => x.IsValid && x.Questions.Count == 0))
                {
                    var pool = _db.QuestionBankQuestions.AsNoTracking().Where(q => q.IsActive && !q.IsTestOnly);
                    if (t.SubjectId.HasValue) { var sid = t.SubjectId.Value; pool = pool.Where(q => q.SubjectId == sid); }
                    if (t.TopicId.HasValue) { var tid = t.TopicId.Value; pool = pool.Where(q => q.TopicId == tid); }
                    if (!string.IsNullOrWhiteSpace(t.Difficulty) && Enum.TryParse<DifficultyLevel>(t.Difficulty, true, out var dl)) pool = pool.Where(q => q.DifficultyLevel == dl);
                    if (t.ExamId.HasValue || t.YearFrom.HasValue || t.YearTo.HasValue)
                    {
                        var eid = t.ExamId; var yf = t.YearFrom; var yt = t.YearTo;
                        pool = pool.Where(q => q.ExamMappings.Any(m => (eid == null || m.ExamId == eid)
                            && (yf == null || m.Year >= yf) && (yt == null || m.Year <= yt)));
                    }
                    var count = await pool.CountAsync();
                    var wanted = t.QuestionCount ?? 20;
                    if (count < wanted)
                        t.Warnings.Add($"Only {count} matching question(s) in the Question Bank right now, but the template asks for {wanted} -- students will get fewer questions until more are added.");
                }
            }
        }

        private void ResolveQuestions(
            TestImportTestDto t,
            Dictionary<string, ExistingQuestionRef> existingByKey,
            Dictionary<Guid, OptionLetter> correctById,
            List<Guid> refIds,
            Dictionary<string, List<BankText>> textLookup)
        {
            var errors = t.Errors;
            var seen = new HashSet<string>();
            var reportedAll = 0;
            void QErr(TestImportQuestionDto q, string msg)
            {
                // Cap per-test noise: a bad column can fail hundreds of rows with the same problem.
                if (reportedAll++ < 15) errors.Add($"Row {q.RowNumber}: {msg}");
                else if (reportedAll == 16) errors.Add("... and more question errors (fix the ones above first).");
            }

            foreach (var q in t.Questions)
            {
                q.Resolution = null;
                q.ResolvedQuestionId = null;
                var hasText = !string.IsNullOrWhiteSpace(q.QuestionText);
                var anyOption = !string.IsNullOrWhiteSpace(q.OptionA) || !string.IsNullOrWhiteSpace(q.OptionB)
                                || !string.IsNullOrWhiteSpace(q.OptionC) || !string.IsNullOrWhiteSpace(q.OptionD);
                var allOptions = !string.IsNullOrWhiteSpace(q.OptionA) && !string.IsNullOrWhiteSpace(q.OptionB)
                                 && !string.IsNullOrWhiteSpace(q.OptionC) && !string.IsNullOrWhiteSpace(q.OptionD);
                string dedupe;

                if (!string.IsNullOrWhiteSpace(q.Ref))
                {
                    if (!Guid.TryParse(q.Ref, out var rid) || !correctById.ContainsKey(rid))
                    { QErr(q, $"Ref \"{q.Ref}\" is not an active Question Bank question id."); continue; }
                    q.Resolution = "Existing";
                    q.ResolvedQuestionId = rid;
                    dedupe = rid.ToString();
                }
                else if (!hasText)
                {
                    QErr(q, "QuestionText is required (or give the Ref of an existing question).");
                    continue;
                }
                else if (anyOption && !allOptions)
                {
                    QErr(q, "Give all four options (OptionA-OptionD), or none of them to reuse an existing question by its text.");
                    continue;
                }
                else if (allOptions)
                {
                    var key = _import.BuildDuplicateKey(q.QuestionText, q.OptionA, q.OptionB, q.OptionC, q.OptionD);
                    q.ResolvedKey = key;
                    var letter = NormalizeCorrect(q.CorrectOption);
                    if (existingByKey.TryGetValue(key, out var found))
                    {
                        q.Resolution = "Existing";
                        q.ResolvedQuestionId = found.Id;
                        dedupe = found.Id.ToString();
                        if (letter != null && correctById.TryGetValue(found.Id, out var stored) && stored.ToString() != letter)
                            t.Warnings.Add($"Row {q.RowNumber}: your file says the answer is {letter}, but the Question Bank already has this question with answer {stored} -- the Question Bank's answer will be used.");
                    }
                    else
                    {
                        if (letter == null) { QErr(q, "CorrectOption must be A, B, C or D."); continue; }
                        q.CorrectOption = letter;
                        if (string.IsNullOrWhiteSpace(q.Subject)) { QErr(q, "Subject is required for a new question."); continue; }
                        if (q.Subject!.Trim().Length > 100 || (q.Topic?.Trim().Length ?? 0) > 150) { QErr(q, "Subject (max 100) or Topic (max 150) is too long."); continue; }
                        if (!string.IsNullOrWhiteSpace(q.Difficulty) && !Enum.TryParse<DifficultyLevel>(q.Difficulty.Trim(), true, out _))
                        { QErr(q, "Difficulty must be Easy, Medium or Hard."); continue; }
                        q.Resolution = "New";
                        dedupe = "key:" + key;
                    }
                }
                else
                {
                    // Text only -> reference an existing question by its text.
                    var norm = _import.NormalizeForDuplicateCheck(q.QuestionText);
                    textLookup.TryGetValue(norm, out var matches);
                    var distinct = matches?.GroupBy(m => m.Key).Select(g => g.First()).ToList() ?? new List<BankText>();
                    if (distinct.Count == 0) { QErr(q, "No such question in the Question Bank. To create it as a new question, add all four options, the correct option and a subject."); continue; }
                    if (distinct.Count > 1) { QErr(q, "This text matches several different questions in the Question Bank -- add the options (or the Ref) to pick one."); continue; }
                    q.Resolution = "Existing";
                    q.ResolvedQuestionId = distinct[0].Id;
                    dedupe = distinct[0].Id.ToString();
                }

                if (!seen.Add(dedupe)) QErr(q, "This question appears twice in the same test.");
            }
        }

        private void ValidateMock(TestImportTestDto t)
        {
            var errors = t.Errors;

            if (!string.IsNullOrWhiteSpace(t.TestType))
            {
                var norm = new string(t.TestType.Where(char.IsLetterOrDigit).ToArray());
                if (!Enum.TryParse<MockTestType>(norm, true, out var tt)) errors.Add("TestType must be FullMockTest, SubjectQuiz, TopicPractice or DailyQuiz.");
                else t.TestType = tt.ToString();
            }
            else t.TestType = MockTestType.FullMockTest.ToString();

            if (t.Questions.Count == 0) errors.Add("A mock test needs at least one question.");
            if (t.Questions.Count > 300) errors.Add("A mock test can have at most 300 questions.");

            // ---- sections ----
            var sections = t.Sections;
            var order = sections.Select((s, i) => (Name: (s.Name ?? string.Empty).Trim(), Index: i + 1)).ToList();
            if (sections.Count > 0)
            {
                var sectionError = MockTestRules.ValidateSections(sections.Select(s => (s.Name ?? string.Empty, s.DurationMinutes)).ToList(), out var isSectional, out var total);
                if (sectionError != null) errors.Add(sectionError);
                else
                {
                    foreach (var q in t.Questions)
                    {
                        var match = order.FirstOrDefault(o => string.Equals(o.Name, q.Section?.Trim(), StringComparison.OrdinalIgnoreCase));
                        if (match.Index == 0)
                        {
                            errors.Add($"Row {q.RowNumber}: section \"{q.Section}\" is not one of the test's sections.");
                            break;
                        }
                        q.SectionOrder = match.Index;
                    }
                    foreach (var o in order)
                        if (t.Questions.All(q => q.SectionOrder != o.Index) && errors.Count == 0)
                            errors.Add($"Section \"{o.Name}\" has no questions.");
                    if (isSectional)
                    {
                        t.TotalMinutes = total;
                        if (t.DurationMinutes.HasValue && t.DurationMinutes.Value != total)
                            t.Warnings.Add($"DurationMinutes ({t.DurationMinutes}) ignored -- a sectioned test's total time is the sum of its sections ({total} min).");
                    }
                }
            }

            if (t.TotalMinutes == 0)
            {
                if (t.DurationMinutes is >= 1 and <= 600) t.TotalMinutes = t.DurationMinutes.Value;
                else errors.Add("DurationMinutes (1-600) is required, or give every section its own time.");
            }

            // ---- dates + result release ----
            DateTime? scheduled = null, end = null, releaseAt = null;
            var dateOk = true;
            if (!TryParseIst(t.ScheduledAt, out scheduled)) { errors.Add("ScheduledAt isn't a valid date (use e.g. 2026-11-05 10:00)."); dateOk = false; }
            if (!TryParseIst(t.EndAt, out end)) { errors.Add("EndAt isn't a valid date (use e.g. 2026-11-12 23:59)."); dateOk = false; }
            if (!TryParseIst(t.ResultReleaseAt, out releaseAt)) { errors.Add("ResultReleaseAt isn't a valid date (use e.g. 2026-11-15 18:00)."); dateOk = false; }
            t.ScheduledAtUtc = scheduled;
            t.EndAtUtc = end;
            t.ResultReleaseAtUtc = releaseAt;
            if (dateOk && scheduled.HasValue && end.HasValue && end <= scheduled) errors.Add("EndAt must be after ScheduledAt.");

            if (!MockTestRules.TryParseReleaseMode(string.IsNullOrWhiteSpace(t.ResultRelease) ? "Instant" : t.ResultRelease, out var mode))
                errors.Add("ResultRelease must be Instant, AfterDelay, OnDate or AfterWindowEnds.");
            else if (dateOk)
            {
                t.ResultRelease = mode.ToString();
                var releaseError = MockTestRules.ValidateResultRelease(mode, t.ResultDelayMinutes, releaseAt, end);
                if (releaseError != null) errors.Add(releaseError);
            }

            if (t.Status == "Published" && !t.ScheduledAtUtc.HasValue)
                t.Warnings.Add("Status is Published with no ScheduledAt -- students can start it as soon as you import.");
        }

        private void ValidatePractice(
            TestImportTestDto t,
            Dictionary<string, Guid> subjects,
            List<TopicInfo> topics)
        {
            var errors = t.Errors;
            if (t.Sections.Count > 0) t.Warnings.Add("Sections are ignored for practice templates.");

            if (!string.IsNullOrWhiteSpace(t.Subject))
            {
                if (subjects.TryGetValue(t.Subject.Trim().ToLowerInvariant(), out var sid)) t.SubjectId = sid;
                else errors.Add($"Subject \"{t.Subject}\" doesn't exist in the Question Bank.");
            }
            if (!string.IsNullOrWhiteSpace(t.Topic))
            {
                var name = t.Topic.Trim();
                var candidates = topics.Where(x => string.Equals(x.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)
                                                   && (!t.SubjectId.HasValue || x.SubjectId == t.SubjectId)).ToList();
                if (candidates.Count == 1) { t.TopicId = candidates[0].Id; t.SubjectId ??= candidates[0].SubjectId; }
                else if (candidates.Count == 0) errors.Add($"Topic \"{t.Topic}\" doesn't exist{(t.SubjectId.HasValue ? " under that subject" : "")}.");
                else errors.Add($"Topic \"{t.Topic}\" exists under several subjects -- add the Subject column.");
            }

            if (!string.IsNullOrWhiteSpace(t.Difficulty))
            {
                if (Enum.TryParse<DifficultyLevel>(t.Difficulty.Trim(), true, out var d)) t.Difficulty = d.ToString();
                else errors.Add("Difficulty must be Easy, Medium or Hard.");
            }
            if (t.YearFrom is < 1900 or > 2100 || t.YearTo is < 1900 or > 2100) errors.Add("YearFrom / YearTo must be between 1900 and 2100.");
            else if (t.YearFrom.HasValue && t.YearTo.HasValue && t.YearFrom > t.YearTo) errors.Add("YearFrom can't be after YearTo.");

            if (t.DurationMinutes is null)
            {
                t.DurationMinutes = 20;
                t.Warnings.Add("DurationMinutes was blank -- using 20.");
            }
            if (t.DurationMinutes is < 1 or > 600) errors.Add("DurationMinutes must be between 1 and 600.");
            t.TotalMinutes = t.DurationMinutes ?? 0;

            if (t.Questions.Count > 0)
            {
                if (t.Questions.Count > 200) errors.Add("A curated practice template can have at most 200 questions.");
                t.QuestionCount = t.Questions.Count;
            }
            else
            {
                t.QuestionCount ??= 20;
                if (t.QuestionCount is < 1 or > 200) errors.Add("QuestionCount must be between 1 and 200.");
            }
        }

        // ---- small helpers ----

        private static string? NormalizeCorrect(string? raw)
        {
            var s = (raw ?? string.Empty).Trim().TrimEnd('.', ')').ToUpperInvariant();
            return s switch { "A" or "1" => "A", "B" or "2" => "B", "C" or "3" => "C", "D" or "4" => "D", _ => null };
        }

        internal static bool TryParseNegative(string? raw, out decimal value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var s = raw.Trim();
            if (s.Contains('/'))
            {
                var parts = s.Split('/');
                if (parts.Length != 2 || !decimal.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var a)
                    || !decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var b) || b == 0) return false;
                value = Math.Round(a / b, 4);
            }
            else if (s.EndsWith('%'))
            {
                if (!decimal.TryParse(s.TrimEnd('%'), NumberStyles.Number, CultureInfo.InvariantCulture, out var p)) return false;
                value = p / 100m;
            }
            else if (!decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out value)) return false;
            return value >= 0 && value <= 1;
        }

        // Blank -> ok with null. Text with an explicit offset / Z is converted to UTC; anything else is kept as typed.
        internal static bool TryParseIst(string? raw, out DateTime? utc)
        {
            utc = null;
            if (string.IsNullOrWhiteSpace(raw)) return true;
            var s = raw.Trim();
            var hasZone = s.EndsWith('Z') || s.EndsWith('z')
                          || System.Text.RegularExpressions.Regex.IsMatch(s, @"[+-]\d{2}:?\d{2}$") && s.Contains('T');
            if (hasZone && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
            {
                utc = dto.UtcDateTime;
                return true;
            }
            string[] formats =
            {
                "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd",
                "dd-MM-yyyy HH:mm", "dd/MM/yyyy HH:mm", "dd-MM-yyyy", "dd/MM/yyyy", "d/M/yyyy H:mm", "d-M-yyyy H:mm"
            };
            if (!DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) return false;
            utc = DateTime.SpecifyKind(local - TypedTimeOffset, DateTimeKind.Utc);
            return true;
        }

        // =============================================================================================
        // COMMIT
        // =============================================================================================

        public async Task<TestImportCommitResultDto> CommitAsync(string token, IReadOnlyCollection<int>? selectedIndexes, Guid adminId)
        {
            var staged = await _cache.GetAsync<TestImportStaged>(CacheKey(token))
                ?? throw new TestImportException("This preview has expired. Upload the file again.", 410);
            if (staged.AdminId != adminId) throw new TestImportException("This preview belongs to another admin.", 403);

            var job = await _db.TestImportJobs.FirstOrDefaultAsync(j => j.Id == staged.JobId)
                ?? throw new TestImportException("Import job not found.", 404);
            if (job.Status != ImportJobStatus.PendingReview) throw new TestImportException("This upload was already imported.", 409);

            var selected = selectedIndexes is { Count: > 0 } ? selectedIndexes.ToHashSet() : null;
            var toImport = staged.Tests.Where(t => t.IsValid && (selected == null || selected.Contains(t.Index))).ToList();
            if (toImport.Count == 0) throw new TestImportException("There are no valid tests to import.");

            var isMock = job.Kind == TestImportKind.MockTest;

            // The preview resolved "Existing" questions minutes ago -- make sure none vanished since.
            var existingIds = toImport.SelectMany(t => t.Questions).Where(q => q.ResolvedQuestionId.HasValue).Select(q => q.ResolvedQuestionId!.Value).Distinct().ToList();
            var stillThere = new HashSet<Guid>();
            foreach (var chunk in existingIds.Chunk(500))
                foreach (var id in await _db.QuestionBankQuestions.Where(q => q.IsActive && chunk.Contains(q.Id)).Select(q => q.Id).ToListAsync())
                    stillThere.Add(id);
            if (existingIds.Any(id => !stillThere.Contains(id)))
                throw new TestImportException("A Question Bank question used by this file was removed after the preview. Upload the file again.", 409);

            // Titles could have been taken since the preview.
            var titles = toImport.Select(t => t.Title).ToList();
            var taken = isMock
                ? await _db.MockTests.Where(m => titles.Contains(m.Title)).Select(m => m.Title).ToListAsync()
                : await _db.PracticeTestTemplates.Where(m => titles.Contains(m.Title)).Select(m => m.Title).ToListAsync();
            if (taken.Count > 0) throw new TestImportException($"A test titled \"{taken[0]}\" now exists. Upload the file again.", 409);

            var subjectCache = new Dictionary<string, QuestionBankSubject>(StringComparer.OrdinalIgnoreCase);
            var topicCache = new Dictionary<(Guid, string), QuestionBankTopic>();
            var newByKey = new Dictionary<string, Guid>();
            var reused = new HashSet<Guid>();
            var created = 0;

            async Task<QuestionBankSubject> SubjectAsync(string name)
            {
                var n = name.Trim();
                if (subjectCache.TryGetValue(n, out var cached)) return cached;
                var found = await _db.QuestionBankSubjects.FirstOrDefaultAsync(s => s.IsActive && s.Name.ToLower() == n.ToLower());
                if (found == null)
                {
                    found = new QuestionBankSubject { Name = n, CreatedByAdminId = adminId };
                    _db.QuestionBankSubjects.Add(found);
                }
                subjectCache[n] = found;
                return found;
            }
            async Task<QuestionBankTopic> TopicAsync(QuestionBankSubject subject, string? name)
            {
                var n = string.IsNullOrWhiteSpace(name) ? "General" : name.Trim();
                if (topicCache.TryGetValue((subject.Id, n.ToLowerInvariant()), out var cached)) return cached;
                var found = await _db.QuestionBankTopics.FirstOrDefaultAsync(t => t.IsActive && t.SubjectId == subject.Id && t.Name.ToLower() == n.ToLower());
                if (found == null)
                {
                    found = new QuestionBankTopic { SubjectId = subject.Id, Name = n, CreatedByAdminId = adminId };
                    _db.QuestionBankTopics.Add(found);
                }
                topicCache[(subject.Id, n.ToLowerInvariant())] = found;
                return found;
            }

            // Returns the Question Bank id to use for the row (creating a test-only question when it is new).
            async Task<Guid> QuestionIdAsync(TestImportTestDto t, TestImportQuestionDto q)
            {
                if (q.ResolvedQuestionId.HasValue) { reused.Add(q.ResolvedQuestionId.Value); return q.ResolvedQuestionId.Value; }
                if (q.ResolvedKey != null && newByKey.TryGetValue(q.ResolvedKey, out var already)) return already;

                var subject = await SubjectAsync(q.Subject!);
                var topic = await TopicAsync(subject, q.Topic);
                var entity = new QuestionBankQuestion
                {
                    QuestionText = q.QuestionText.Trim(),
                    NormalizedQuestionText = _import.NormalizeForDuplicateCheck(q.QuestionText),
                    OptionA = q.OptionA.Trim(),
                    OptionB = q.OptionB.Trim(),
                    OptionC = q.OptionC.Trim(),
                    OptionD = q.OptionD.Trim(),
                    CorrectOption = Enum.Parse<OptionLetter>(q.CorrectOption, true),
                    Explanation = string.IsNullOrWhiteSpace(q.Explanation) ? null : q.Explanation.Trim(),
                    DifficultyLevel = !string.IsNullOrWhiteSpace(q.Difficulty) && Enum.TryParse<DifficultyLevel>(q.Difficulty, true, out var dl) ? dl : DifficultyLevel.Medium,
                    Language = ParseLanguage(t.Language),
                    SubjectId = subject.Id,
                    TopicId = topic.Id,
                    IsTestOnly = true,
                    TestImportJobId = job.Id,
                    CreatedByAdminId = adminId,
                    CreatedAt = DateTime.UtcNow
                };
                _db.QuestionBankQuestions.Add(entity);
                if (q.ResolvedKey != null) newByKey[q.ResolvedKey] = entity.Id;
                created++;
                return entity.Id;
            }

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                foreach (var t in toImport)
                {
                    if (isMock) await AddMockAsync(t, job.Id, adminId, QuestionIdAsync);
                    else await AddPracticeAsync(t, job.Id, adminId, QuestionIdAsync);
                    await _db.SaveChangesAsync();
                }

                job.Status = ImportJobStatus.Committed;
                job.CommittedAt = DateTime.UtcNow;
                job.ImportedTests = toImport.Count;
                job.NewQuestionsCreated = created;
                job.ExistingQuestionsReused = reused.Count;
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }

            await _cache.RemoveAsync(CacheKey(token));
            await _audit.LogAsync(adminId, isMock ? "MockTest.BulkImport" : "PracticeTest.BulkImport", "TestImportJob", job.Id,
                $"{toImport.Count} test(s), {created} new question(s)");

            return new TestImportCommitResultDto
            {
                JobId = job.Id,
                ImportedTests = toImport.Count,
                NewQuestionsCreated = created,
                ExistingQuestionsReused = reused.Count
            };
        }

        private async Task AddMockAsync(TestImportTestDto t, Guid jobId, Guid adminId, Func<TestImportTestDto, TestImportQuestionDto, Task<Guid>> questionId)
        {
            var sectionInput = t.Sections.Select(s => (s.Name ?? string.Empty, s.DurationMinutes)).ToList();
            MockTestRules.ValidateSections(sectionInput, out var isSectional, out var total);
            MockTestRules.TryParseReleaseMode(string.IsNullOrWhiteSpace(t.ResultRelease) ? "Instant" : t.ResultRelease, out var mode);

            var test = new MockTest
            {
                Title = t.Title,
                ExamName = t.ExamName ?? t.Exam ?? string.Empty,
                ExamId = t.ExamId,
                TestType = Enum.Parse<MockTestType>(t.TestType ?? nameof(MockTestType.FullMockTest), true),
                DurationMinutes = isSectional ? total : t.TotalMinutes,
                NegativeMarkingRatio = t.NegativeMarkingValue,
                IsRandomOrder = t.RandomOrder ?? false,
                IsShuffleOptions = t.ShuffleOptions ?? false,
                Language = ParseLanguage(t.Language),
                ScheduledAt = t.ScheduledAtUtc,
                EndAt = t.EndAtUtc,
                Status = Enum.Parse<TestPublishStatus>(t.Status ?? "Draft", true),
                MaxAttempts = t.MaxAttempts,
                Instructions = t.Instructions,
                IsSectional = isSectional,
                ResultReleaseMode = mode,
                ResultDelayMinutes = mode == ResultReleaseMode.AfterDelay ? t.ResultDelayMinutes : null,
                ResultReleaseAt = mode == ResultReleaseMode.OnDate ? t.ResultReleaseAtUtc : null,
                ImportJobId = jobId,
                CreatedByAdminId = adminId,
                CreatedAt = DateTime.UtcNow
            };
            _db.MockTests.Add(test);

            var sections = t.Sections.Select((s, i) => new MockTestSection
            {
                MockTestId = test.Id,
                Name = s.Name.Trim(),
                SectionOrder = i + 1,
                DurationMinutes = s.DurationMinutes
            }).ToList();
            if (sections.Count > 0) _db.MockTestSections.AddRange(sections);

            // Sections first (in their order), then the file order inside each one.
            var ordered = t.Questions.OrderBy(q => q.SectionOrder).ThenBy(q => q.RowNumber).ToList();
            var n = 0;
            foreach (var q in ordered)
            {
                var id = await questionId(t, q);
                _db.MockTestQuestions.Add(new MockTestQuestion
                {
                    MockTestId = test.Id,
                    QuestionBankQuestionId = id,
                    QuestionOrder = ++n,
                    SectionId = sections.Count > 0 && q.SectionOrder > 0 ? sections[q.SectionOrder - 1].Id : null
                });
            }
        }

        private async Task AddPracticeAsync(TestImportTestDto t, Guid jobId, Guid adminId, Func<TestImportTestDto, TestImportQuestionDto, Task<Guid>> questionId)
        {
            var template = new PracticeTestTemplate
            {
                Title = t.Title,
                Description = t.Description,
                SubjectId = t.SubjectId,
                TopicId = t.TopicId,
                ExamId = t.ExamId,
                YearFrom = t.YearFrom,
                YearTo = t.YearTo,
                Difficulty = !string.IsNullOrWhiteSpace(t.Difficulty) && Enum.TryParse<DifficultyLevel>(t.Difficulty, true, out var d) ? d : null,
                QuestionCount = t.QuestionCount ?? 20,
                DurationMinutes = t.TotalMinutes,
                NegativeMarkingRatio = t.NegativeMarkingValue,
                IsRandomOrder = t.RandomOrder ?? true,
                Status = Enum.Parse<TestPublishStatus>(t.Status ?? "Draft", true),
                ImportJobId = jobId,
                CreatedByAdminId = adminId,
                CreatedAt = DateTime.UtcNow
            };
            _db.PracticeTestTemplates.Add(template);

            var n = 0;
            foreach (var q in t.Questions.OrderBy(q => q.RowNumber))
            {
                var id = await questionId(t, q);
                _db.PracticeTestTemplateQuestions.Add(new PracticeTestTemplateQuestion
                {
                    PracticeTestTemplateId = template.Id,
                    QuestionBankQuestionId = id,
                    QuestionOrder = ++n
                });
            }
        }

        private static PaperLanguage? ParseLanguage(string? raw) =>
            !string.IsNullOrWhiteSpace(raw) && Enum.TryParse<PaperLanguage>(raw.Trim(), true, out var l) ? l : null;

        // =============================================================================================
        // ROLLBACK / HISTORY
        // =============================================================================================

        public async Task<TestImportRollbackResultDto> RollbackAsync(Guid jobId, Guid adminId)
        {
            var job = await _db.TestImportJobs.FirstOrDefaultAsync(j => j.Id == jobId)
                ?? throw new TestImportException("Import not found.", 404);
            if (job.Status != ImportJobStatus.Committed) throw new TestImportException("Only a completed import can be rolled back.", 409);

            var isMock = job.Kind == TestImportKind.MockTest;
            List<Guid> testIds;
            if (isMock)
            {
                var tests = await _db.MockTests.Where(t => t.ImportJobId == jobId).Select(t => new { t.Id, t.Title }).ToListAsync();
                testIds = tests.Select(t => t.Id).ToList();
                var attempted = await _db.StudentTestResults.Where(r => r.MockTestId != null && testIds.Contains(r.MockTestId.Value))
                    .Select(r => r.MockTestId!.Value).Distinct().ToListAsync();
                if (attempted.Count > 0)
                    throw new TestImportException($"Can't roll back: students have already attempted {attempted.Count} of these tests (for example \"{tests.First(t => attempted.Contains(t.Id)).Title}\"). Archive them instead.", 409);
            }
            else
            {
                var tests = await _db.PracticeTestTemplates.Where(t => t.ImportJobId == jobId).Select(t => new { t.Id, t.Title }).ToListAsync();
                testIds = tests.Select(t => t.Id).ToList();
                var attempted = await _db.StudentTestResults.Where(r => r.PracticeTestTemplateId != null && testIds.Contains(r.PracticeTestTemplateId.Value))
                    .Select(r => r.PracticeTestTemplateId!.Value).Distinct().ToListAsync();
                if (attempted.Count > 0)
                    throw new TestImportException($"Can't roll back: students have already attempted {attempted.Count} of these templates (for example \"{tests.First(t => attempted.Contains(t.Id)).Title}\"). Archive them instead.", 409);
            }

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                if (isMock)
                {
                    await _db.MockTestQuestions.Where(q => testIds.Contains(q.MockTestId)).ExecuteDeleteAsync();
                    await _db.MockTestSections.Where(s => testIds.Contains(s.MockTestId)).ExecuteDeleteAsync();
                    await _db.MockTests.Where(t => testIds.Contains(t.Id)).ExecuteDeleteAsync();
                }
                else
                {
                    await _db.PracticeTestTemplateQuestions.Where(q => testIds.Contains(q.PracticeTestTemplateId)).ExecuteDeleteAsync();
                    await _db.PracticeTestTemplates.Where(t => testIds.Contains(t.Id)).ExecuteDeleteAsync();
                }

                // Test-only questions this job created -- unless something else now uses them (another test
                // that reused them, or a student's attempt), in which case they stay.
                var candidates = await _db.QuestionBankQuestions
                    .Where(q => q.TestImportJobId == jobId && q.IsTestOnly).Select(q => q.Id).ToListAsync();
                var removed = 0;
                foreach (var chunk in candidates.Chunk(500))
                {
                    removed += await _db.QuestionBankQuestions
                        .Where(q => chunk.Contains(q.Id)
                            && !_db.MockTestQuestions.Any(m => m.QuestionBankQuestionId == q.Id)
                            && !_db.PracticeTestTemplateQuestions.Any(m => m.QuestionBankQuestionId == q.Id)
                            && !_db.StudentAnswers.Any(a => a.QuestionBankQuestionId == q.Id))
                        .ExecuteDeleteAsync();
                }

                job.Status = ImportJobStatus.RolledBack;
                job.RolledBackAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                await _audit.LogAsync(adminId, isMock ? "MockTest.BulkImport.Rollback" : "PracticeTest.BulkImport.Rollback", "TestImportJob", jobId,
                    $"{testIds.Count} test(s), {removed} question(s)");
                return new TestImportRollbackResultDto { TestsRemoved = testIds.Count, QuestionsRemoved = removed, QuestionsKept = candidates.Count - removed };
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task<List<TestImportJobDto>> HistoryAsync(TestImportKind? kind)
        {
            var q = _db.TestImportJobs.AsNoTracking().Where(j => j.Status != ImportJobStatus.PendingReview);
            if (kind.HasValue) q = q.Where(j => j.Kind == kind.Value);
            var jobs = await q.OrderByDescending(j => j.CreatedAt).Take(100)
                .Select(j => new TestImportJobDto
                {
                    Id = j.Id,
                    Kind = j.Kind.ToString(),
                    Format = j.Format.ToString(),
                    FileName = j.FileName,
                    Status = j.Status.ToString(),
                    TotalTests = j.TotalTests,
                    ValidTests = j.ValidTests,
                    InvalidTests = j.InvalidTests,
                    ImportedTests = j.ImportedTests,
                    NewQuestionsCreated = j.NewQuestionsCreated,
                    ExistingQuestionsReused = j.ExistingQuestionsReused,
                    CreatedByAdminName = j.CreatedByAdmin != null ? j.CreatedByAdmin.FullName : null,
                    CreatedAt = j.CreatedAt,
                    CommittedAt = j.CommittedAt,
                    RolledBackAt = j.RolledBackAt
                }).ToListAsync();
            return jobs;
        }

        public async Task<TestImportJobDto?> GetJobAsync(Guid jobId)
        {
            var job = (await HistoryAsync(null)).FirstOrDefault(j => j.Id == jobId);
            if (job == null)
            {
                // Older than the 100 most recent rows -- fall back to a direct lookup.
                var j = await _db.TestImportJobs.AsNoTracking().Include(x => x.CreatedByAdmin).FirstOrDefaultAsync(x => x.Id == jobId && x.Status != ImportJobStatus.PendingReview);
                if (j == null) return null;
                job = new TestImportJobDto
                {
                    Id = j.Id, Kind = j.Kind.ToString(), Format = j.Format.ToString(), FileName = j.FileName, Status = j.Status.ToString(),
                    TotalTests = j.TotalTests, ValidTests = j.ValidTests, InvalidTests = j.InvalidTests, ImportedTests = j.ImportedTests,
                    NewQuestionsCreated = j.NewQuestionsCreated, ExistingQuestionsReused = j.ExistingQuestionsReused,
                    CreatedByAdminName = j.CreatedByAdmin?.FullName, CreatedAt = j.CreatedAt, CommittedAt = j.CommittedAt, RolledBackAt = j.RolledBackAt
                };
            }

            if (job.Status == nameof(ImportJobStatus.Committed))
            {
                job.Tests = job.Kind == nameof(TestImportKind.MockTest)
                    ? await _db.MockTests.AsNoTracking().Where(t => t.ImportJobId == jobId).OrderBy(t => t.CreatedAt)
                        .Select(t => new TestImportJobTestDto { Id = t.Id, BusinessId = t.BusinessId, Title = t.Title, Status = t.Status.ToString(), QuestionCount = t.MockTestQuestions.Count }).ToListAsync()
                    : await _db.PracticeTestTemplates.AsNoTracking().Where(t => t.ImportJobId == jobId).OrderBy(t => t.CreatedAt)
                        .Select(t => new TestImportJobTestDto { Id = t.Id, BusinessId = t.BusinessId, Title = t.Title, Status = t.Status.ToString(), QuestionCount = t.Questions.Count > 0 ? t.Questions.Count : t.QuestionCount }).ToListAsync();
            }
            return job;
        }
    }
}
