using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // SCORAM_TESTS -- everything here is identical regardless of whether the attempt is a Practice
    // Test or a Mock Test (see StudentTestResult.TestKind): once an attempt exists, answering,
    // auto-saving, submitting, and resuming it work exactly the same way. Only CREATING an attempt
    // differs (MockTestsController.Start vs PracticeTestsController.Generate/StartFromTemplate).
    [ApiController]
    [Route("api/tests/attempts")]
    [Authorize(Roles = "Student")]
    public class TestAttemptsController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly IGamificationService _gamification;
        private readonly INotificationService _notifications;

        public TestAttemptsController(ScoramDbContext db, IGamificationService gamification, INotificationService notifications)
        {
            _db = db;
            _gamification = gamification;
            _notifications = notifications;
        }

        // GET /api/tests/attempts/{attemptId} -- if still InProgress, this IS "resume": returns the
        // same shape as starting, with whatever answers/marks-for-review were already auto-saved. If
        // already submitted, returns the full graded result instead (spec: "Resume Test" / a
        // submitted attempt can't be modified).
        [HttpGet("{attemptId:guid}")]
        public async Task<ActionResult<object>> GetAttempt(Guid attemptId)
        {
            var userId = User.GetUserId();
            var attempt = await LoadOwnedAttemptAsync(attemptId, userId);
            if (attempt == null) return NotFound(new { message = "Attempt not found." });

            return attempt.Status == TestAttemptStatus.InProgress
                ? Ok(ToStartResponse(attempt))
                : Ok(ToResultDto(attempt));
        }

        // PATCH /api/tests/attempts/answers/{studentAnswerId} -- auto-save. Fire-and-forget-friendly:
        // the frontend calls this on every answer change / Mark for Review toggle / Clear Response,
        // typically debounced, without waiting for it to block navigation to the next question.
        [HttpPatch("answers/{studentAnswerId:guid}")]
        public async Task<ActionResult<TestAnswerSaveResponseDto>> SaveAnswer(Guid studentAnswerId, TestAnswerSaveDto dto)
        {
            var userId = User.GetUserId();

            var answer = await _db.StudentAnswers
                .Include(a => a.StudentTestResult)
                .FirstOrDefaultAsync(a => a.Id == studentAnswerId && a.StudentTestResult!.UserId == userId);
            if (answer == null) return NotFound();

            if (answer.StudentTestResult!.Status != TestAttemptStatus.InProgress)
                return BadRequest(new { message = "This attempt has already been submitted and can't be changed." });

            // SelectedOption is nullable-and-present-vs-absent matters: the DTO always carries the
            // field (frontend always sends it, even as null for "Clear Response"), so we always
            // apply it here rather than trying to distinguish "omitted" from "explicitly cleared".
            if (dto.SelectedOption != null)
            {
                if (!Enum.TryParse<OptionLetter>(dto.SelectedOption, ignoreCase: true, out var parsed))
                    return BadRequest(new { message = $"'{dto.SelectedOption}' isn't a valid option." });
                answer.SelectedOption = parsed;
                answer.AnsweredAt = DateTime.UtcNow;
            }
            else
            {
                answer.SelectedOption = null;
                answer.AnsweredAt = null;
            }

            if (dto.IsMarkedForReview.HasValue) answer.IsMarkedForReview = dto.IsMarkedForReview.Value;

            await _db.SaveChangesAsync();

            return Ok(new TestAnswerSaveResponseDto
            {
                Id = answer.Id,
                SelectedOption = answer.SelectedOption?.ToString(),
                IsMarkedForReview = answer.IsMarkedForReview,
                AnsweredAt = answer.AnsweredAt
            });
        }

        // POST /api/tests/attempts/{attemptId}/submit -- grades server-side from whatever was
        // auto-saved (never trusts a score/answers payload from the client). Idempotent: submitting
        // an already-submitted attempt again just returns the existing result instead of re-grading
        // or erroring, so a double-click or a retried request after a flaky network response can't
        // produce two submissions or corrupt the stored result.
        [HttpPost("{attemptId:guid}/submit")]
        public async Task<ActionResult<TestSubmitResultDto>> Submit(Guid attemptId, TestSubmitDto dto)
        {
            var userId = User.GetUserId();
            var attempt = await LoadOwnedAttemptAsync(attemptId, userId);
            if (attempt == null) return NotFound(new { message = "Attempt not found." });

            if (attempt.Status != TestAttemptStatus.InProgress)
                return Ok(ToResultDto(attempt)); // idempotent -- already graded, hand back what's there

            GradeAttempt(attempt, TestAttemptStatus.Submitted, dto.TimeTakenSeconds);
            await _db.SaveChangesAsync();

            // GAMIFICATION -- completing either kind of test counts as today's practice: XP + streak
            // together. Exam-wise leaderboard tagging only applies to Mock Tests (MockTest.ExamName is
            // always set); Practice Tests optionally carry one too when generated from a template tied
            // to a specific Exam, otherwise this is just null and the XP simply doesn't count toward
            // any exam-wise leaderboard (still counts toward Global and Friends).
            var examName = attempt.TestKind switch
            {
                TestKind.Mock => attempt.MockTest?.ExamName,
                TestKind.PreviousYearPaper => attempt.Paper?.Exam?.Name,
                // A Weak Topics Quiz spans whichever subjects were weak, not one Exam -- no
                // exam-wise leaderboard tagging for it, same as an untargeted ad-hoc Practice attempt.
                TestKind.Quiz => null,
                _ => attempt.PracticeTestTemplate?.Exam?.Name
            };
            var reason = attempt.TestKind switch
            {
                TestKind.Mock => GamificationService.Reasons.MockTestCompleted,
                TestKind.PreviousYearPaper => GamificationService.Reasons.PreviousYearPaperCompleted,
                TestKind.Quiz => GamificationService.Reasons.QuizCompleted,
                _ => GamificationService.Reasons.PracticeTestCompleted
            };
            await _gamification.RecordActivityAsync(userId, GamificationService.XpFor(reason), reason, examName);

            // Phase 3, Challenge a Friend -- if THIS attempt is what a friend accepted a challenge
            // with, let the original challenger know it's done instead of them having to remember to
            // check back (see QuizChallengesController for the "challenge sent" notification's other
            // half).
            if (attempt.TestKind == TestKind.Quiz)
            {
                var challenge = await _db.QuizChallenges.FirstOrDefaultAsync(c => c.ChallengedAttemptId == attempt.Id);
                if (challenge != null)
                {
                    var challengedUser = await _db.Users.FindAsync(challenge.ChallengedUserId);
                    var challengedName = challengedUser?.FullName ?? "Your friend";
                    await _notifications.CreateAsync(
                        challenge.ChallengerUserId,
                        NotificationType.QuizChallenge,
                        $"{challengedName} finished your challenge!",
                        $"They scored {attempt.Score} -- see how you compare.",
                        "/quizzes"
                    );
                }
            }

            return Ok(ToResultDto(attempt));
        }

        // GET /api/tests/attempts/mine?status=&page=&pageSize= -- "My Tests": In Progress + Completed,
        // Practice and Mock mixed together (spec: student navigation section).
        [HttpGet("mine")]
        public async Task<ActionResult<PagedResult<MyTestAttemptSummaryDto>>> Mine(
            [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var userId = User.GetUserId();
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _db.StudentTestResults
                .Include(r => r.Answers)
                .Include(r => r.MockTest)
                .Include(r => r.PracticeTestTemplate)
                .Include(r => r.Paper).ThenInclude(p => p!.Exam)
                .Include(r => r.Quiz)
                .Where(r => r.UserId == userId)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TestAttemptStatus>(status, true, out var parsedStatus))
                query = query.Where(r => r.Status == parsedStatus);

            query = query.OrderByDescending(r => r.StartedAt);

            var totalCount = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            var now = DateTime.UtcNow;
            var mapped = new List<MyTestAttemptSummaryDto>();
            foreach (var r in items)
            {
                var title = TitleFor(r);

                mapped.Add(new MyTestAttemptSummaryDto
                {
                    AttemptId = r.Id,
                    TestKind = r.TestKind.ToString(),
                    Title = title,
                    Status = r.Status.ToString(),
                    Score = r.Status == TestAttemptStatus.InProgress ? null : r.Score,
                    PercentageScore = ComputePercentage(r),
                    AccuracyPercent = ComputeAccuracy(r),
                    TimeTakenSeconds = r.TimeTakenSeconds,
                    StartedAt = r.StartedAt,
                    SubmittedAt = r.Status == TestAttemptStatus.InProgress ? null : r.AttemptedAt,
                    CanResume = r.Status == TestAttemptStatus.InProgress
                });
            }

            return Ok(new PagedResult<MyTestAttemptSummaryDto> { Items = mapped, TotalCount = totalCount, Page = page, PageSize = pageSize });
        }

        // ======================================================================================
        // Helpers -- also used by MockTestsController/PracticeTestsController for consistent
        // grading/shaping wherever they hand back an in-progress or completed attempt.
        // ======================================================================================

        private async Task<Models.StudentTestResult?> LoadOwnedAttemptAsync(Guid attemptId, Guid userId)
        {
            return await _db.StudentTestResults
                .Include(r => r.Answers)
                .Include(r => r.MockTest)
                .Include(r => r.PracticeTestTemplate).ThenInclude(t => t!.Exam)
                .Include(r => r.Paper).ThenInclude(p => p!.Exam)
                .Include(r => r.Quiz)
                .FirstOrDefaultAsync(r => r.Id == attemptId && r.UserId == userId);
        }

        // Server-authoritative grading -- called only from Submit above (and the auto-submit path
        // once the timer expires, see MockTestsController/PracticeTestsController). Never accepts
        // scores/correctness from the client; correctness is always recomputed here from each
        // answer's own frozen CorrectOptionSnapshot.
        internal static void GradeAttempt(Models.StudentTestResult attempt, TestAttemptStatus finalStatus, int timeTakenSeconds)
        {
            int correct = 0, wrong = 0, skipped = 0;
            foreach (var a in attempt.Answers)
            {
                if (a.SelectedOption == null)
                {
                    skipped++;
                    a.IsCorrect = false;
                }
                else
                {
                    a.IsCorrect = a.SelectedOption.Value == a.CorrectOptionSnapshot;
                    if (a.IsCorrect) correct++; else wrong++;
                }
            }

            attempt.CorrectCount = correct;
            attempt.WrongCount = wrong;
            attempt.SkippedCount = skipped;
            attempt.Score = correct - (wrong * attempt.NegativeMarkingRatio);
            attempt.TimeTakenSeconds = timeTakenSeconds;
            attempt.Status = finalStatus;
            attempt.AttemptedAt = DateTime.UtcNow;
        }

        internal static TestAttemptStartResponseDto ToStartResponse(Models.StudentTestResult attempt)
        {
            var durationMinutes = attempt.TestKind switch
            {
                TestKind.Mock => attempt.MockTest?.DurationMinutes ?? 0,
                TestKind.PreviousYearPaper => attempt.Paper?.DurationMinutes ?? 0,
                TestKind.Quiz => attempt.Quiz?.DurationMinutes ?? attempt.QuizDurationMinutes ?? EstimateDurationMinutes(attempt),
                _ => attempt.PracticeTestTemplate?.DurationMinutes ?? attempt.PracticeDurationMinutes ?? EstimateDurationMinutes(attempt)
            };

            return new TestAttemptStartResponseDto
            {
                AttemptId = attempt.Id,
                TestKind = attempt.TestKind.ToString(),
                Title = TitleFor(attempt),
                DurationMinutes = durationMinutes,
                NegativeMarkingRatio = attempt.NegativeMarkingRatio,
                StartedAt = attempt.StartedAt,
                ExpiresAt = attempt.StartedAt.AddMinutes(durationMinutes),
                Instructions = attempt.MockTest?.Instructions ?? attempt.PracticeTestTemplate?.Description,
                Questions = attempt.Answers
                    .OrderBy(a => a.QuestionOrder)
                    .Select(a => new TestAttemptQuestionDto
                    {
                        Id = a.Id,
                        QuestionOrder = a.QuestionOrder,
                        QuestionText = a.QuestionTextSnapshot,
                        OptionA = a.OptionASnapshot,
                        OptionB = a.OptionBSnapshot,
                        OptionC = a.OptionCSnapshot,
                        OptionD = a.OptionDSnapshot,
                        QuestionImageUrl = a.QuestionImageUrlSnapshot,
                        OptionAImageUrl = a.OptionAImageUrlSnapshot,
                        OptionBImageUrl = a.OptionBImageUrlSnapshot,
                        OptionCImageUrl = a.OptionCImageUrlSnapshot,
                        OptionDImageUrl = a.OptionDImageUrlSnapshot,
                        ContentBlocks = ContentBlocksJsonHelper.Parse(a.ContentBlocksJsonSnapshot),
                        SelectedOption = a.SelectedOption?.ToString(),
                        IsMarkedForReview = a.IsMarkedForReview
                    }).ToList()
            };
        }

        // Shared "what do we call this attempt" logic -- used by ToStartResponse, ToResultDto, and
        // Mine() below so all three always agree on a title for the same attempt.
        internal static string TitleFor(Models.StudentTestResult attempt) => attempt.TestKind switch
        {
            TestKind.Mock => attempt.MockTest?.Title ?? "Mock Test",
            TestKind.PreviousYearPaper => PaperTitleFor(attempt.Paper) ?? "Previous Year Paper",
            TestKind.Quiz => attempt.Quiz?.Title ?? QuizTitleFor(attempt) ?? "Quiz",
            _ => attempt.PracticeTestTemplate?.Title ?? "Practice Test"
        };

        // "Reasoning & Quant Quiz" -- there's no title to look up (no Quiz template entity exists,
        // see TestKind.Quiz's own comment), so this is derived from whichever subjects the quiz
        // actually ended up covering, straight off each answer's own SubjectSnapshot.
        private static string? QuizTitleFor(Models.StudentTestResult attempt)
        {
            var subjects = attempt.Answers
                .Select(a => a.SubjectSnapshot)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .Take(2)
                .ToList();
            return subjects.Count > 0 ? $"{string.Join(" & ", subjects)} Quiz" : null;
        }

        private static string? PaperTitleFor(Models.Paper? paper)
        {
            if (paper == null) return null;
            var parts = new List<string> { paper.Exam?.Name ?? "Exam", paper.Year.ToString() };
            if (!string.IsNullOrWhiteSpace(paper.PaperCode)) parts.Add(paper.PaperCode!);
            return string.Join(" \u2013 ", parts);
        }

        // Ad-hoc Practice attempts with no template carry their own DurationMinutes nowhere on the
        // model today -- PracticeTestsController stamps it via a matching-shaped fallback; this only
        // triggers if that somehow wasn't set, so it never surfaces a zero-minute timer.
        private static int EstimateDurationMinutes(Models.StudentTestResult attempt) =>
            Math.Max(5, attempt.Answers.Count); // ~1 minute/question floor, only as a last resort

        internal static TestSubmitResultDto ToResultDto(Models.StudentTestResult attempt)
        {
            var total = attempt.Answers.Count;

            return new TestSubmitResultDto
            {
                AttemptId = attempt.Id,
                TestKind = attempt.TestKind.ToString(),
                Title = TitleFor(attempt),
                Score = attempt.Score,
                MaxPossibleScore = total,
                TotalQuestions = total,
                CorrectCount = attempt.CorrectCount,
                WrongCount = attempt.WrongCount,
                SkippedCount = attempt.SkippedCount,
                AccuracyPercent = ComputeAccuracy(attempt) ?? 0,
                PercentageScore = ComputePercentage(attempt) ?? 0,
                TimeTakenSeconds = attempt.TimeTakenSeconds,
                Rank = attempt.Rank,
                Percentile = null, // no cross-student ranking pool computed yet -- see plan notes
                AttemptedAt = attempt.AttemptedAt,
                Questions = attempt.Answers
                    .OrderBy(a => a.QuestionOrder)
                    .Select(a => new TestAnswerReviewDto
                    {
                        StudentAnswerId = a.Id,
                        QuestionOrder = a.QuestionOrder,
                        QuestionText = a.QuestionTextSnapshot,
                        OptionA = a.OptionASnapshot,
                        OptionB = a.OptionBSnapshot,
                        OptionC = a.OptionCSnapshot,
                        OptionD = a.OptionDSnapshot,
                        CorrectOption = a.CorrectOptionSnapshot.ToString(),
                        QuestionImageUrl = a.QuestionImageUrlSnapshot,
                        OptionAImageUrl = a.OptionAImageUrlSnapshot,
                        OptionBImageUrl = a.OptionBImageUrlSnapshot,
                        OptionCImageUrl = a.OptionCImageUrlSnapshot,
                        OptionDImageUrl = a.OptionDImageUrlSnapshot,
                        ExplanationImageUrl = a.ExplanationImageUrlSnapshot,
                        ContentBlocks = ContentBlocksJsonHelper.Parse(a.ContentBlocksJsonSnapshot),
                        SelectedOption = a.SelectedOption?.ToString(),
                        IsCorrect = a.IsCorrect,
                        WasSkipped = a.SelectedOption == null,
                        Explanation = a.ExplanationSnapshot,
                        Subject = a.SubjectSnapshot,
                        Topic = a.TopicSnapshot,
                        SourceQuestionId = a.QuestionId,
                        SourceQuestionBankQuestionId = a.QuestionBankQuestionId,
                        IsQuestionBank = a.QuestionBankQuestionId != null
                    }).ToList()
            };
        }

        private static decimal? ComputePercentage(Models.StudentTestResult r)
        {
            if (r.Status == TestAttemptStatus.InProgress) return null;
            var total = r.Answers.Count;
            return total == 0 ? 0 : Math.Round(r.Score / total * 100, 2);
        }

        private static decimal? ComputeAccuracy(Models.StudentTestResult r)
        {
            if (r.Status == TestAttemptStatus.InProgress) return null;
            var attempted = r.CorrectCount + r.WrongCount;
            return attempted == 0 ? 0 : Math.Round((decimal)r.CorrectCount / attempted * 100, 2);
        }
    }
}
