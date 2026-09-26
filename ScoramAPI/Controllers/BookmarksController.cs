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
        public Task<ActionResult<BookmarkToggleResponseDto>> ToggleQuestion(Guid questionId) =>
            ToggleAsync(
                b => b.QuestionId == questionId,
                () => new Bookmark { QuestionId = questionId },
                () => _db.Questions.AnyAsync(q => q.Id == questionId),
                "Question not found.");

        // POST /api/question-bank/{questionId}/bookmark
        [HttpPost("question-bank/{questionId:guid}/bookmark")]
        public Task<ActionResult<BookmarkToggleResponseDto>> ToggleQuestionBankQuestion(Guid questionId) =>
            ToggleAsync(
                b => b.QuestionBankQuestionId == questionId,
                () => new Bookmark { QuestionBankQuestionId = questionId },
                () => _db.QuestionBankQuestions.AnyAsync(q => q.Id == questionId && q.IsActive),
                "Question not found.");

        // POST /api/comments/{commentId}/bookmark -- commentId must be a TOP-LEVEL comment (a whole
        // thread), same rule DiscussionsController's own feed follows. Deliberately under
        // "comments/", not "discussions/" -- matches every other per-comment action already in
        // DiscussionsController (upvote/downvote/resolve/pin/report all live at
        // comments/{commentId}/..., "discussions" is reserved there for the top-level feed route).
        [HttpPost("comments/{commentId:guid}/bookmark")]
        public Task<ActionResult<BookmarkToggleResponseDto>> ToggleDiscussion(Guid commentId) =>
            ToggleAsync(
                b => b.CommentId == commentId,
                () => new Bookmark { CommentId = commentId },
                () => _db.QuestionComments.AnyAsync(c => c.Id == commentId && c.ParentCommentId == null),
                "Discussion thread not found.");

        // POST /api/papers/{paperId}/bookmark
        [HttpPost("papers/{paperId:guid}/bookmark")]
        public Task<ActionResult<BookmarkToggleResponseDto>> TogglePaper(Guid paperId) =>
            ToggleAsync(
                b => b.PaperId == paperId,
                () => new Bookmark { PaperId = paperId },
                () => _db.Papers.AnyAsync(p => p.Id == paperId),
                "Paper not found.");

        // POST /api/mocktests/{mockTestId}/bookmark
        [HttpPost("mocktests/{mockTestId:guid}/bookmark")]
        public Task<ActionResult<BookmarkToggleResponseDto>> ToggleMockTest(Guid mockTestId) =>
            ToggleAsync(
                b => b.MockTestId == mockTestId,
                () => new Bookmark { MockTestId = mockTestId },
                () => _db.MockTests.AnyAsync(m => m.Id == mockTestId),
                "Mock test not found.");

        // targetExistsAsync is only ever consulted on the ADD path below -- REMOVING a bookmark is
        // always allowed regardless of the target's current state. Without that split, a
        // QuestionBankQuestion an admin later deactivates (IsActive = false) would permanently trap
        // a student's bookmark of it: the existence check would 404 on every toggle attempt,
        // including the one trying to remove it, leaving it stuck on their Bookmarks page forever
        // with no way to clear it.
        private async Task<ActionResult<BookmarkToggleResponseDto>> ToggleAsync(
            System.Linq.Expressions.Expression<Func<Bookmark, bool>> matchesTarget,
            Func<Bookmark> makeNewBookmark,
            Func<Task<bool>> targetExistsAsync,
            string notFoundMessage)
        {
            var userId = User.GetUserId();

            var existing = await _db.Bookmarks.Where(matchesTarget).FirstOrDefaultAsync(b => b.UserId == userId);

            if (existing != null)
            {
                _db.Bookmarks.Remove(existing);
                await _db.SaveChangesAsync();
                return Ok(new BookmarkToggleResponseDto { IsBookmarked = false });
            }

            if (!await targetExistsAsync())
                return NotFound(new { message = notFoundMessage });

            var bookmark = makeNewBookmark();
            bookmark.UserId = userId;
            _db.Bookmarks.Add(bookmark);
            await _db.SaveChangesAsync();
            return Ok(new BookmarkToggleResponseDto { IsBookmarked = true });
        }

        // GET /api/bookmarks?type=all|questions|discussions|papers|mocktests&page=1&pageSize=20
        // Unified, most-recent-first list across every type this student has saved. Combined and
        // paged in memory after five separate per-type queries (each needs its own join/projection,
        // so one single SQL query across all five isn't practical) -- but each of those queries is
        // capped to the top page*pageSize most-recent rows of its own type (see each branch's
        // .Take(page * pageSize) below), not the type's entire history: no single type can ever
        // contribute more than page*pageSize items to the true combined top-page*pageSize, so this
        // cap can't exclude anything the requested page actually needs, regardless of how deep the
        // pagination goes or how many bookmarks a long-time user has accumulated. TotalCount is a
        // separate COUNT query rather than derived from these capped lists -- see its own comment.
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
                    .OrderByDescending(b => b.CreatedAt)
                    .Take(page * pageSize)
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
                    .OrderByDescending(b => b.CreatedAt)
                    .Take(page * pageSize)
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
                    .OrderByDescending(b => b.CreatedAt)
                    .Take(page * pageSize)
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
                    .OrderByDescending(b => b.CreatedAt)
                    .Take(page * pageSize)
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
                    .OrderByDescending(b => b.CreatedAt)
                    .Take(page * pageSize)
                    .ToListAsync();
                items.AddRange(mockTests);
            }

            // Computed separately from the (now capped, see each branch's .Take(page * pageSize)
            // above) items list rather than derived from its count -- that cap is what keeps this
            // endpoint from loading a power user's entire bookmark history into memory just to
            // answer one page, but it means `items.Count` no longer reflects the true total once
            // someone has more bookmarks than that. A plain COUNT query, by contrast, stays cheap
            // regardless of how many bookmarks exist (every Bookmark row belongs to exactly one
            // target type, per this file's own comment on the filtered unique indexes below, so
            // "all" needs only one count -- no target-type breakdown required).
            var totalCount = type switch
            {
                "questions" => await _db.Bookmarks.CountAsync(b => b.UserId == userId && (b.QuestionId != null || b.QuestionBankQuestionId != null)),
                "discussions" => await _db.Bookmarks.CountAsync(b => b.UserId == userId && b.CommentId != null),
                "papers" => await _db.Bookmarks.CountAsync(b => b.UserId == userId && b.PaperId != null),
                "mocktests" => await _db.Bookmarks.CountAsync(b => b.UserId == userId && b.MockTestId != null),
                _ => await _db.Bookmarks.CountAsync(b => b.UserId == userId)
            };

            var ordered = items.OrderByDescending(i => i.CreatedAt).ToList();
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
