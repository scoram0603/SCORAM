using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Models;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    [ApiController]
    [Route("api")]
    public class DiscussionsController : ControllerBase
    {
        private static readonly Regex MentionPattern = new(@"@([a-z0-9._]{3,30})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly ScoramDbContext _db;
        private readonly IAdminPermissionService _permissions;
        private readonly INotificationService _notifications;
        private readonly IAuditLogService _audit;

        public DiscussionsController(ScoramDbContext db, IAdminPermissionService permissions, INotificationService notifications, IAuditLogService audit)
        {
            _db = db;
            _permissions = permissions;
            _notifications = notifications;
            _audit = audit;
        }

        // GET /api/discussions?page=1&pageSize=20
        // Global feed across all questions, most-upvoted first -- this is what powers a "Top
        // Discussions" list/sidebar, not tied to one question. Legacy-Question comments only for now
        // (SCORAM_QUESTION_BANK comments don't have a Paper/ExamName to show here); Question Bank's
        // own discussion still shows fine on its question detail page via ListForQuestionBank below.
        [HttpGet("discussions")]
        public async Task<ActionResult<PagedResult<DiscussionFeedItemDto>>> TopDiscussions(int page = 1, int pageSize = 20)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 100);

            // Null when the request isn't authenticated -- guards the IsBookmarked subquery below
            // the same way every other viewer-specific field in this codebase (MyVote, etc.) does.
            var userId = User.Identity?.IsAuthenticated == true ? User.GetUserId() : (Guid?)null;

            var query = _db.QuestionComments
                .Where(c => c.ParentCommentId == null && c.QuestionId != null)
                .Include(c => c.Question).ThenInclude(q => q!.Paper).ThenInclude(p => p!.Exam)
                .Include(c => c.User)
                .Include(c => c.SubmittedByAdmin);

            var totalCount = await query.CountAsync();

            var items = await query
                .OrderByDescending(c => c.UpvoteCount)
                .ThenByDescending(c => c.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new DiscussionFeedItemDto
                {
                    CommentId = c.Id,
                    QuestionId = c.QuestionId!.Value,
                    ExamName = c.Question!.Paper != null ? (c.Question!.Paper!.Exam != null ? c.Question!.Paper!.Exam!.Name : "Unknown") : (c.Question!.ExamName ?? "Unknown"),
                    Subject = c.Question!.Subject,
                    CommentText = c.CommentText,
                    AuthorName = c.SubmittedByAdmin != null ? c.SubmittedByAdmin.FullName : (c.User != null ? c.User.FullName : "Unknown"),
                    UpvoteCount = c.UpvoteCount,
                    ReplyCount = _db.QuestionComments.Count(r => r.ParentCommentId == c.Id),
                    CreatedAt = c.CreatedAt,
                    IsBookmarked = userId != null && _db.Bookmarks.Any(b => b.CommentId == c.Id && b.UserId == userId)
                })
                .ToListAsync();

            return Ok(new PagedResult<DiscussionFeedItemDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }

        // GET /api/questions/{questionId}/comments -- full thread, arbitrary depth.
        [HttpGet("questions/{questionId:guid}/comments")]
        public async Task<ActionResult<List<CommentResponseDto>>> ListForQuestion(Guid questionId)
        {
            var questionExists = await _db.Questions.AnyAsync(q => q.Id == questionId);
            if (!questionExists) return NotFound(new { message = "Question not found." });

            return Ok(await BuildThreadAsync(c => c.QuestionId == questionId));
        }

        // GET /api/question-bank/{questionId}/comments -- same "Discussion" feature (section 28's
        // sibling for comments, following the same reuse-not-duplicate approach as Report/Solution),
        // reusing this exact thread-building logic for Question Bank questions.
        [HttpGet("question-bank/{questionId:guid}/comments")]
        public async Task<ActionResult<List<CommentResponseDto>>> ListForQuestionBank(Guid questionId)
        {
            var questionExists = await _db.QuestionBankQuestions.AnyAsync(q => q.Id == questionId && q.IsActive);
            if (!questionExists) return NotFound(new { message = "Question not found." });

            return Ok(await BuildThreadAsync(c => c.QuestionBankQuestionId == questionId));
        }

        private async Task<List<CommentResponseDto>> BuildThreadAsync(System.Linq.Expressions.Expression<Func<QuestionComment, bool>> matchesQuestion)
        {
            var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
            Guid? currentUserId = isAuthenticated ? User.GetUserId() : null;

            var allComments = await _db.QuestionComments
                .Where(matchesQuestion)
                .Include(c => c.User)
                .Include(c => c.SubmittedByAdmin)
                .OrderByDescending(c => c.IsPinned)
                .ThenByDescending(c => c.UpvoteCount)
                .ThenBy(c => c.CreatedAt)
                .ToListAsync();

            // One query for every vote this user has cast in this thread, rather than one query per
            // comment -- keeps a busy thread's response to a single extra round trip.
            Dictionary<Guid, bool> myVotesByCommentId = new();
            if (currentUserId.HasValue && allComments.Count > 0)
            {
                var commentIds = allComments.Select(c => c.Id).ToList();
                myVotesByCommentId = await _db.CommentVotes
                    .Where(v => v.UserId == currentUserId.Value && commentIds.Contains(v.CommentId))
                    .ToDictionaryAsync(v => v.CommentId, v => v.IsUpvote);
            }

            var childrenByParent = allComments
                .Where(c => c.ParentCommentId != null)
                .GroupBy(c => c.ParentCommentId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            List<CommentResponseDto> BuildTree(IEnumerable<QuestionComment> level) =>
                level.Select(c => ToResponseDto(
                    c,
                    c.SubmittedByAdmin != null ? c.SubmittedByAdmin.FullName : (c.User != null ? c.User.FullName : "Unknown"),
                    authorIsAdmin: c.SubmittedByAdminId != null,
                    isMine: currentUserId != null && c.UserId == currentUserId,
                    myVote: myVotesByCommentId.TryGetValue(c.Id, out var v) ? v : (bool?)null,
                    replies: childrenByParent.TryGetValue(c.Id, out var children) ? BuildTree(children) : new List<CommentResponseDto>()
                )).ToList();

            var topLevel = allComments.Where(c => c.ParentCommentId == null);
            return BuildTree(topLevel);
        }

        // POST /api/questions/{questionId}/comments
        [HttpPost("questions/{questionId:guid}/comments")]
        [Authorize(Roles = "Student")]
        public async Task<ActionResult<CommentResponseDto>> Create(Guid questionId, CommentCreateDto dto)
        {
            var questionExists = await _db.Questions.AnyAsync(q => q.Id == questionId);
            if (!questionExists) return NotFound(new { message = "Question not found." });

            return await CreateCommentAsync(dto, c => c.QuestionId = questionId, $"/questions/{questionId}");
        }

        // POST /api/question-bank/{questionId}/comments
        [HttpPost("question-bank/{questionId:guid}/comments")]
        [Authorize(Roles = "Student")]
        public async Task<ActionResult<CommentResponseDto>> CreateForQuestionBank(Guid questionId, CommentCreateDto dto)
        {
            var questionExists = await _db.QuestionBankQuestions.AnyAsync(q => q.Id == questionId && q.IsActive);
            if (!questionExists) return NotFound(new { message = "Question not found." });

            return await CreateCommentAsync(dto, c => c.QuestionBankQuestionId = questionId, $"/question-bank/{questionId}");
        }

        private async Task<ActionResult<CommentResponseDto>> CreateCommentAsync(CommentCreateDto dto, Action<QuestionComment> assignQuestion, string mentionLinkPath)
        {
            if (string.IsNullOrWhiteSpace(dto.CommentText))
                return BadRequest(new { message = "Comment text is required." });

            var userId = User.GetUserId();
            var user = await _db.Users.FindAsync(userId);
            if (user == null) return Unauthorized();

            var comment = new QuestionComment
            {
                UserId = userId,
                CommentText = dto.CommentText,
                CreatedAt = DateTime.UtcNow
            };
            assignQuestion(comment);

            _db.QuestionComments.Add(comment);
            await _db.SaveChangesAsync();
            await NotifyMentionsAsync(comment, user.FullName, userId, mentionLinkPath);

            return Ok(ToResponseDto(comment, user.FullName, authorIsAdmin: false, isMine: true, myVote: null, replies: new List<CommentResponseDto>()));
        }

        // POST /api/comments/{commentId}/replies -- commentId can be a top-level comment OR another
        // reply; either way this just becomes a child of whatever was passed in, at whatever depth
        // that puts it. Works for both legacy and Question Bank threads: it copies whichever of
        // QuestionId/QuestionBankQuestionId the parent has, rather than needing a separate route.
        [HttpPost("comments/{commentId:guid}/replies")]
        [Authorize(Roles = "Student")]
        public async Task<ActionResult<CommentResponseDto>> Reply(Guid commentId, CommentCreateDto dto)
        {
            var parent = await _db.QuestionComments.FindAsync(commentId);
            if (parent == null) return NotFound(new { message = "Comment not found." });

            if (string.IsNullOrWhiteSpace(dto.CommentText))
                return BadRequest(new { message = "Reply text is required." });

            var userId = User.GetUserId();
            var user = await _db.Users.FindAsync(userId);
            if (user == null) return Unauthorized();

            var reply = new QuestionComment
            {
                QuestionId = parent.QuestionId,
                QuestionBankQuestionId = parent.QuestionBankQuestionId,
                UserId = userId,
                ParentCommentId = parent.Id,
                CommentText = dto.CommentText,
                CreatedAt = DateTime.UtcNow
            };

            _db.QuestionComments.Add(reply);
            await _db.SaveChangesAsync();

            var linkPath = parent.QuestionBankQuestionId != null ? $"/question-bank/{parent.QuestionBankQuestionId}" : $"/questions/{parent.QuestionId}";
            await NotifyMentionsAsync(reply, user.FullName, userId, linkPath);

            return Ok(ToResponseDto(reply, user.FullName, authorIsAdmin: false, isMine: true, myVote: null, replies: new List<CommentResponseDto>()));
        }

        // POST /api/admin/questions/{questionId}/comments/{parentCommentId?} -- an official admin
        // reply. Always IsAdminHighlighted.
        [HttpPost("admin/questions/{questionId:guid}/comments/{parentCommentId:guid?}")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<ActionResult<CommentResponseDto>> CreateByAdmin(Guid questionId, Guid? parentCommentId, CommentCreateDto dto)
        {
            var questionExists = await _db.Questions.AnyAsync(q => q.Id == questionId);
            if (!questionExists) return NotFound(new { message = "Question not found." });

            return await CreateByAdminAsync(dto, parentCommentId, c => c.QuestionId = questionId, "Question", questionId, $"/questions/{questionId}");
        }

        // POST /api/admin/question-bank/{questionId}/comments/{parentCommentId?}
        [HttpPost("admin/question-bank/{questionId:guid}/comments/{parentCommentId:guid?}")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<ActionResult<CommentResponseDto>> CreateByAdminForQuestionBank(Guid questionId, Guid? parentCommentId, CommentCreateDto dto)
        {
            var questionExists = await _db.QuestionBankQuestions.AnyAsync(q => q.Id == questionId && q.IsActive);
            if (!questionExists) return NotFound(new { message = "Question not found." });

            return await CreateByAdminAsync(dto, parentCommentId, c => c.QuestionBankQuestionId = questionId, "QuestionBankQuestion", questionId, $"/question-bank/{questionId}");
        }

        private async Task<ActionResult<CommentResponseDto>> CreateByAdminAsync(
            CommentCreateDto dto, Guid? parentCommentId, Action<QuestionComment> assignQuestion,
            string auditTargetType, Guid auditTargetId, string mentionLinkPath)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ModerateDiscussions))
                return Forbid();

            if (string.IsNullOrWhiteSpace(dto.CommentText))
                return BadRequest(new { message = "Reply text is required." });

            if (parentCommentId.HasValue && !await _db.QuestionComments.AnyAsync(c => c.Id == parentCommentId.Value))
                return NotFound(new { message = "Parent comment not found." });

            var adminId = User.GetAdminId();
            var admin = await _db.Admins.FindAsync(adminId);
            if (admin == null) return Unauthorized();

            var comment = new QuestionComment
            {
                SubmittedByAdminId = adminId,
                ParentCommentId = parentCommentId,
                CommentText = dto.CommentText,
                IsAdminHighlighted = true,
                CreatedAt = DateTime.UtcNow
            };
            assignQuestion(comment);

            _db.QuestionComments.Add(comment);
            await _db.SaveChangesAsync();
            await NotifyMentionsAsync(comment, admin.FullName, mentionerUserId: null, mentionLinkPath);
            await _audit.LogAsync(adminId, "Comment.CreateByAdmin", auditTargetType, auditTargetId,
                dto.CommentText.Length > 100 ? dto.CommentText[..100] + "…" : dto.CommentText);

            return Ok(ToResponseDto(comment, admin.FullName, authorIsAdmin: true, isMine: false, myVote: null, replies: new List<CommentResponseDto>()));
        }

        // POST /api/comments/{commentId}/upvote  -- proper per-user toggle now (CommentVote table):
        // same vote again removes it, the opposite vote switches it, no vote yet creates it. Fixes a
        // known prior gap where this was just an unrestricted counter++ anyone could click forever.
        [HttpPost("comments/{commentId:guid}/upvote")]
        [Authorize(Roles = "Student")]
        public async Task<ActionResult<object>> Upvote(Guid commentId) => await ApplyCommentVoteAsync(commentId, isUpvote: true);

        // POST /api/comments/{commentId}/downvote -- same toggle, opposite direction.
        [HttpPost("comments/{commentId:guid}/downvote")]
        [Authorize(Roles = "Student")]
        public async Task<ActionResult<object>> Downvote(Guid commentId) => await ApplyCommentVoteAsync(commentId, isUpvote: false);

        private async Task<ActionResult<object>> ApplyCommentVoteAsync(Guid commentId, bool isUpvote)
        {
            var comment = await _db.QuestionComments.FindAsync(commentId);
            if (comment == null) return NotFound();

            var userId = User.GetUserId();
            var existing = await _db.CommentVotes.FirstOrDefaultAsync(v => v.CommentId == commentId && v.UserId == userId);

            bool? myVote;
            if (existing == null)
            {
                _db.CommentVotes.Add(new CommentVote { CommentId = commentId, UserId = userId, IsUpvote = isUpvote });
                if (isUpvote) comment.UpvoteCount++; else comment.DownvoteCount++;
                myVote = isUpvote;
            }
            else if (existing.IsUpvote == isUpvote)
            {
                // Clicking the same direction again retracts the vote.
                _db.CommentVotes.Remove(existing);
                if (isUpvote) comment.UpvoteCount = Math.Max(0, comment.UpvoteCount - 1);
                else comment.DownvoteCount = Math.Max(0, comment.DownvoteCount - 1);
                myVote = null;
            }
            else
            {
                // Switching from upvote to downvote or vice versa.
                existing.IsUpvote = isUpvote;
                if (isUpvote) { comment.UpvoteCount++; comment.DownvoteCount = Math.Max(0, comment.DownvoteCount - 1); }
                else { comment.DownvoteCount++; comment.UpvoteCount = Math.Max(0, comment.UpvoteCount - 1); }
                myVote = isUpvote;
            }

            await _db.SaveChangesAsync();
            return Ok(new { comment.Id, comment.UpvoteCount, comment.DownvoteCount, myVote });
        }

        // PATCH /api/comments/{commentId}/resolve -- toggled by the top-level comment's own author or
        // any admin. Unchanged: already generic by commentId, works for either question type.
        [HttpPatch("comments/{commentId:guid}/resolve")]
        [Authorize]
        public async Task<IActionResult> ToggleResolved(Guid commentId)
        {
            var comment = await _db.QuestionComments.FindAsync(commentId);
            if (comment == null) return NotFound();
            if (comment.ParentCommentId != null) return BadRequest(new { message = "Only a top-level comment can be marked resolved." });

            var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
            var isAuthor = !isAdmin && User.Identity?.IsAuthenticated == true && comment.UserId == User.GetUserId();
            if (!isAdmin && !isAuthor) return Forbid();

            comment.IsResolved = !comment.IsResolved;
            await _db.SaveChangesAsync();

            return Ok(new { comment.Id, comment.IsResolved });
        }

        // PATCH /api/comments/{commentId}/pin
        [HttpPatch("comments/{commentId:guid}/pin")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> Pin(Guid commentId)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ModerateDiscussions))
                return Forbid();

            var comment = await _db.QuestionComments.FindAsync(commentId);
            if (comment == null) return NotFound();

            comment.IsPinned = !comment.IsPinned;
            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), comment.IsPinned ? "Comment.Pin" : "Comment.Unpin", "Comment", commentId);

            return Ok(new { comment.Id, comment.IsPinned });
        }

        // POST /api/comments/{commentId}/report
        [HttpPost("comments/{commentId:guid}/report")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> Report(Guid commentId, CommentReportCreateDto dto)
        {
            var commentExists = await _db.QuestionComments.AnyAsync(c => c.Id == commentId);
            if (!commentExists) return NotFound();

            _db.CommentReports.Add(new CommentReport
            {
                CommentId = commentId,
                ReportedByUserId = User.GetUserId(),
                Reason = dto.Reason
            });
            await _db.SaveChangesAsync();

            return Ok(new { message = "Thanks -- a moderator will take a look." });
        }

        // GET /api/admin/comment-reports/pending
        [HttpGet("admin/comment-reports/pending")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<ActionResult<PagedResult<ReportedCommentDto>>> GetPendingReports([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ModerateDiscussions))
                return Forbid();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _db.CommentReports
                .Include(r => r.Comment).ThenInclude(c => c!.User)
                .Include(r => r.Comment).ThenInclude(c => c!.SubmittedByAdmin)
                .Include(r => r.Comment).ThenInclude(c => c!.QuestionBankQuestion)
                .Include(r => r.ReportedByUser)
                .Where(r => r.Status == ReportStatus.Pending)
                .OrderBy(r => r.CreatedAt);

            var totalCount = await query.CountAsync();
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(r => new ReportedCommentDto
                {
                    ReportId = r.Id,
                    CommentId = r.CommentId,
                    QuestionId = r.Comment!.QuestionId,
                    QuestionBankQuestionId = r.Comment.QuestionBankQuestionId,
                    IsQuestionBank = r.Comment.QuestionBankQuestionId != null,
                    QuestionTextSnippet = r.Comment.QuestionBankQuestionId != null && r.Comment.QuestionBankQuestion != null
                        ? (r.Comment.QuestionBankQuestion.QuestionText.Length > 140 ? r.Comment.QuestionBankQuestion.QuestionText.Substring(0, 140) + "…" : r.Comment.QuestionBankQuestion.QuestionText)
                        : "",
                    CommentText = r.Comment.CommentText,
                    AuthorName = r.Comment.SubmittedByAdmin != null ? r.Comment.SubmittedByAdmin.FullName : (r.Comment.User != null ? r.Comment.User.FullName : "Unknown"),
                    ReportedByName = r.ReportedByUser != null ? r.ReportedByUser.FullName : "Unknown",
                    Reason = r.Reason,
                    CreatedAt = r.CreatedAt
                })
                .ToListAsync();

            // Legacy-Question snippet is looked up separately rather than joined above -- keeps the
            // main query simpler; Question Bank's snippet is already filled in above via its own
            // Include since that relationship is required to load anyway for IsQuestionBank context.
            var legacyIds = items.Where(i => !i.IsQuestionBank && i.QuestionId.HasValue).Select(i => i.QuestionId!.Value).ToList();
            if (legacyIds.Count > 0)
            {
                var questionTexts = await _db.Questions
                    .Where(q => legacyIds.Contains(q.Id))
                    .Select(q => new { q.Id, q.QuestionText })
                    .ToDictionaryAsync(q => q.Id, q => q.QuestionText);
                foreach (var item in items.Where(i => !i.IsQuestionBank))
                {
                    if (item.QuestionId.HasValue && questionTexts.TryGetValue(item.QuestionId.Value, out var text))
                        item.QuestionTextSnippet = text.Length > 140 ? text[..140] + "…" : text;
                }
            }

            return Ok(new PagedResult<ReportedCommentDto> { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize });
        }

        // PATCH /api/admin/comment-reports/{reportId}/dismiss
        [HttpPatch("admin/comment-reports/{reportId:guid}/dismiss")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> DismissReport(Guid reportId)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ModerateDiscussions))
                return Forbid();

            var report = await _db.CommentReports.FindAsync(reportId);
            if (report == null) return NotFound();

            report.Status = ReportStatus.Rejected;
            report.ResolvedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        // DELETE /api/admin/comment-reports/{reportId}/remove-comment -- collects and removes the
        // whole reported subtree, scoped to whichever question the reported comment actually belongs
        // to (legacy or Question Bank).
        [HttpDelete("admin/comment-reports/{reportId:guid}/remove-comment")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> RemoveReportedComment(Guid reportId)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ModerateDiscussions))
                return Forbid();

            var report = await _db.CommentReports.FindAsync(reportId);
            if (report == null) return NotFound();

            var comment = await _db.QuestionComments.FindAsync(report.CommentId);
            if (comment != null)
            {
                var allInQuestion = comment.QuestionBankQuestionId != null
                    ? await _db.QuestionComments.Where(c => c.QuestionBankQuestionId == comment.QuestionBankQuestionId).ToListAsync()
                    : await _db.QuestionComments.Where(c => c.QuestionId == comment.QuestionId).ToListAsync();

                var toRemove = new List<QuestionComment>();
                void CollectSubtree(Guid id)
                {
                    var node = allInQuestion.FirstOrDefault(c => c.Id == id);
                    if (node == null) return;
                    toRemove.Add(node);
                    foreach (var child in allInQuestion.Where(c => c.ParentCommentId == id))
                        CollectSubtree(child.Id);
                }
                CollectSubtree(comment.Id);

                var removedIds = toRemove.Select(c => c.Id).ToList();
                _db.QuestionComments.RemoveRange(toRemove);

                var otherReports = await _db.CommentReports
                    .Where(r => removedIds.Contains(r.CommentId) && r.Status == ReportStatus.Pending)
                    .ToListAsync();
                foreach (var r in otherReports) { r.Status = ReportStatus.Resolved; r.ResolvedAt = DateTime.UtcNow; }

                await _db.SaveChangesAsync();
                await _audit.LogAsync(User.GetAdminId(), "Comment.RemoveReported", "Comment", report.CommentId,
                    $"{comment.CommentText[..Math.Min(100, comment.CommentText.Length)]}{(comment.CommentText.Length > 100 ? "…" : "")} ({toRemove.Count} comment(s) removed with replies)");
            }

            return NoContent();
        }

        // Parses @username mentions out of a comment/reply, resolves them to real users, and notifies
        // each one. linkPath points the notification at the right detail page -- /questions/{id} for a
        // legacy comment, /question-bank/{id} for a Question Bank one.
        private async Task NotifyMentionsAsync(QuestionComment comment, string authorName, Guid? mentionerUserId, string linkPath)
        {
            var usernames = MentionPattern.Matches(comment.CommentText)
                .Select(m => m.Groups[1].Value.ToLowerInvariant())
                .Distinct()
                .ToList();
            if (usernames.Count == 0) return;

            var mentionedUserIds = await _db.Users
                .Where(u => usernames.Contains(u.Username.ToLower()) && u.Id != mentionerUserId)
                .Select(u => u.Id)
                .ToListAsync();

            foreach (var userId in mentionedUserIds)
            {
                await _notifications.CreateAsync(
                    userId,
                    NotificationType.Mention,
                    $"{authorName} mentioned you",
                    comment.CommentText.Length > 120 ? comment.CommentText[..120] + "…" : comment.CommentText,
                    linkPath
                );
            }
        }

        private static CommentResponseDto ToResponseDto(QuestionComment c, string authorName, bool authorIsAdmin, bool isMine, bool? myVote, List<CommentResponseDto> replies) => new CommentResponseDto
        {
            Id = c.Id,
            QuestionId = c.QuestionId,
            QuestionBankQuestionId = c.QuestionBankQuestionId,
            ParentCommentId = c.ParentCommentId,
            CommentText = c.CommentText,
            AuthorName = authorName,
            AuthorIsAdmin = authorIsAdmin,
            UpvoteCount = c.UpvoteCount,
            DownvoteCount = c.DownvoteCount,
            MyVote = myVote,
            IsPinned = c.IsPinned,
            IsAdminHighlighted = c.IsAdminHighlighted,
            IsResolved = c.IsResolved,
            IsMine = isMine,
            CreatedAt = c.CreatedAt,
            Replies = replies
        };
    }
}
