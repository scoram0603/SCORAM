using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Extensions;
using ScoramAPI.Models;

namespace ScoramAPI.Controllers
{
    // Save-for-later on five different content types, all backed by one Bookmark table (see
    // Models/Bookmark.cs). One toggle route per type -- same shape as QuestionVotesController's
    // per-type vote routes -- plus a single unified GET for the "My Bookmarks" page.
    [ApiController]
    [Route("api")]
    [Authorize(Roles = "Student")]
    public class BookmarksController : ControllerBase
    {
        private readonly ScoramDbContext _db;

        public BookmarksController(ScoramDbContext db)
        {
            _db = db;
        }

        // POST /api/questions/{questionId}/bookmark
        [HttpPost("questions/{questionId:guid}/bookmark")]
        public async Task<ActionResult<BookmarkToggleResponseDto>> ToggleQuestion(Guid questionId)
        {
            if (!await _db.Questions.AnyAsync(q => q.Id == questionId))
                return NotFound(new { message = "Question not found." });

            return await ToggleAsync(b => b.QuestionId == questionId, () => new Bookmark { QuestionId = questionId });
        }

        // POST /api/question-bank/{questionId}/bookmark
        [HttpPost("question-bank/{questionId:guid}/bookmark")]
        public async Task<ActionResult<BookmarkToggleResponseDto>> ToggleQuestionBankQuestion(Guid questionId)
        {
            if (!await _db.QuestionBankQuestions.AnyAsync(q => q.Id == questionId && q.IsActive))
                return NotFound(new { message = "Question not found." });

            return await ToggleAsync(b => b.QuestionBankQuestionId == questionId, () => new Bookmark { QuestionBankQuestionId = questionId });
        }

        // POST /api/discussions/{commentId}/bookmark -- commentId must be a TOP-LEVEL comment
        // (a whole thread), same rule DiscussionsController's own feed follows.
        [HttpPost("discussions/{commentId:guid}/bookmark")]
        public async Task<ActionResult<BookmarkToggleResponseDto>> ToggleDiscussion(Guid commentId)
        {
            var isTopLevelThread = await _db.QuestionComments.AnyAsync(c => c.Id == commentId && c.ParentCommentId == null);
            if (!isTopLevelThread)
                return NotFound(new { message = "Discussion thread not found." });

            return await ToggleAsync(b => b.CommentId == commentId, () => new Bookmark { CommentId = commentId });
        }

        // POST /api/papers/{paperId}/bookmark
        [HttpPost("papers/{paperId:guid}/bookmark")]
        public async Task<ActionResult<BookmarkToggleResponseDto>> TogglePaper(Guid paperId)
        {
            if (!await _db.Papers.AnyAsync(p => p.Id == paperId))
                return NotFound(new { message = "Paper not found." });

            return await ToggleAsync(b => b.PaperId == paperId, () => new Bookmark { PaperId = paperId });
        }

        // POST /api/mocktests/{mockTestId}/bookmark
        [HttpPost("mocktests/{mockTestId:guid}/bookmark")]
        public async Task<ActionResult<BookmarkToggleResponseDto>> ToggleMockTest(Guid mockTestId)
        {
            if (!await _db.MockTests.AnyAsync(m => m.Id == mockTestId))
                return NotFound(new { message = "Mock test not found." });

            return await ToggleAsync(b => b.MockTestId == mockTestId, () => new Bookmark { MockTestId = mockTestId });
        }

        private async Task<ActionResult<BookmarkToggleResponseDto>> ToggleAsync(
            System.Linq.Expressions.Expression<Func<Bookmark, bool>> matchesTarget,
            Func<Bookmark> makeNewBookmark)
        {
            var userId = User.GetUserId();

            var existing = await _db.Bookmarks.Where(matchesTarget).FirstOrDefaultAsync(b => b.UserId == userId);

            if (existing == null)
            {
                var bookmark = makeNewBookmark();
                bookmark.UserId = userId;
                _db.Bookmarks.Add(bookmark);
                await _db.SaveChangesAsync();
                return Ok(new BookmarkToggleResponseDto { IsBookmarked = true });
            }

            _db.Bookmarks.Remove(existing);
            await _db.SaveChangesAsync();
            return Ok(new BookmarkToggleResponseDto { IsBookmarked = false });
        }

        // GET /api/bookmarks?type=all|questions|discussions|papers|mocktests&page=1&pageSize=20
        // Unified, most-recent-first list across every type this student has saved. Client-side
        // paging happens after the union because each type needs a different join/projection --
        // cheap here given a single student's bookmark count is always small.
        [HttpGet("bookmarks")]
        public async Task<ActionResult<PagedResult<BookmarkListItemDto>>> List(string type = "all", int page = 1, int pageSize = 20)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 100);
            var userId = User.GetUserId();

            var items = new List<BookmarkListItemDto>();

            if (type is "all" or "questions")
            {
                var legacy = await _db.Bookmarks
                    .Where(b => b.UserId == userId && b.QuestionId != null)
                    .Include(b => b.Question)
                    .Select(b => new BookmarkListItemDto
                    {
                        BookmarkId = b.Id,
                        Type = BookmarkType.Question,
                        CreatedAt = b.CreatedAt,
                        TargetId = b.QuestionId!.Value,
                        QuestionText = b.Question!.QuestionText,
                        Subject = b.Question!.Subject
                    })
                    .ToListAsync();
                items.AddRange(legacy);

                var bankQuestions = await _db.Bookmarks
                    .Where(b => b.UserId == userId && b.QuestionBankQuestionId != null)
                    .Include(b => b.QuestionBankQuestion!).ThenInclude(q => q.Subject)
                    .Select(b => new BookmarkListItemDto
                    {
                        BookmarkId = b.Id,
                        Type = BookmarkType.QuestionBankQuestion,
                        CreatedAt = b.CreatedAt,
                        TargetId = b.QuestionBankQuestionId!.Value,
                        QuestionText = b.QuestionBankQuestion!.QuestionText,
                        Subject = b.QuestionBankQuestion!.Subject != null ? b.QuestionBankQuestion!.Subject!.Name : null
                    })
                    .ToListAsync();
                items.AddRange(bankQuestions);
            }

            if (type is "all" or "discussions")
            {
                var discussions = await _db.Bookmarks
                    .Where(b => b.UserId == userId && b.CommentId != null)
                    .Include(b => b.Comment!).ThenInclude(c => c.User)
                    .Include(b => b.Comment!).ThenInclude(c => c.SubmittedByAdmin)
                    .Select(b => new BookmarkListItemDto
                    {
                        BookmarkId = b.Id,
                        Type = BookmarkType.Discussion,
                        CreatedAt = b.CreatedAt,
                        TargetId = b.CommentId!.Value,
                        CommentText = b.Comment!.CommentText,
                        AuthorName = b.Comment!.SubmittedByAdmin != null ? b.Comment!.SubmittedByAdmin!.FullName
                            : (b.Comment!.User != null ? b.Comment!.User!.FullName : "Unknown"),
                        ReplyCount = _db.QuestionComments.Count(r => r.ParentCommentId == b.CommentId),
                        DiscussionQuestionId = b.Comment!.QuestionId,
                        DiscussionQuestionBankQuestionId = b.Comment!.QuestionBankQuestionId
                    })
                    .ToListAsync();
                items.AddRange(discussions);
            }

            if (type is "all" or "papers")
            {
                var papers = await _db.Bookmarks
                    .Where(b => b.UserId == userId && b.PaperId != null)
                    .Include(b => b.Paper!).ThenInclude(p => p.Exam)
                    .Select(b => new BookmarkListItemDto
                    {
                        BookmarkId = b.Id,
                        Type = BookmarkType.Paper,
                        CreatedAt = b.CreatedAt,
                        TargetId = b.PaperId!.Value,
                        ExamName = b.Paper!.Exam != null ? b.Paper!.Exam!.Name : "Unknown",
                        Year = b.Paper!.Year,
                        PaperCode = b.Paper!.PaperCode
                    })
                    .ToListAsync();
                items.AddRange(papers);
            }

            if (type is "all" or "mocktests")
            {
                var mockTests = await _db.Bookmarks
                    .Where(b => b.UserId == userId && b.MockTestId != null)
                    .Include(b => b.MockTest)
                    .Select(b => new BookmarkListItemDto
                    {
                        BookmarkId = b.Id,
                        Type = BookmarkType.MockTest,
                        CreatedAt = b.CreatedAt,
                        TargetId = b.MockTestId!.Value,
                        Title = b.MockTest!.Title,
                        ExamName = b.MockTest!.ExamName,
                        DurationMinutes = b.MockTest!.DurationMinutes
                    })
                    .ToListAsync();
                items.AddRange(mockTests);
            }

            var ordered = items.OrderByDescending(i => i.CreatedAt).ToList();
            var totalCount = ordered.Count;
            var pageItems = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return Ok(new PagedResult<BookmarkListItemDto>
            {
                Items = pageItems,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }
    }
}
