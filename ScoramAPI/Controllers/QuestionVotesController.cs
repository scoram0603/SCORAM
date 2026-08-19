using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Extensions;
using ScoramAPI.Models;

namespace ScoramAPI.Controllers
{
    // Like/Dislike on a QUESTION itself -- distinct from a CommentVote on one reply in its discussion
    // thread (DiscussionsController). One user, one vote, per question: clicking the same reaction
    // again retracts it; clicking the other one switches it. Shared by both the legacy Paper-based
    // Question and the new QuestionBankQuestion via the same QuestionVote table (see
    // Models/QuestionModels.cs) so both behave identically instead of two parallel implementations.
    [ApiController]
    [Route("api")]
    [Authorize(Roles = "Student")]
    public class QuestionVotesController : ControllerBase
    {
        private readonly ScoramDbContext _db;

        public QuestionVotesController(ScoramDbContext db)
        {
            _db = db;
        }

        // POST /api/questions/{questionId}/vote  { "isLike": true }
        [HttpPost("questions/{questionId:guid}/vote")]
        public async Task<ActionResult<QuestionVoteResponseDto>> VoteOnQuestion(Guid questionId, QuestionVoteRequestDto dto)
        {
            if (!await _db.Questions.AnyAsync(q => q.Id == questionId))
                return NotFound(new { message = "Question not found." });

            return await ApplyVoteAsync(dto.IsLike, v => v.QuestionId == questionId, () => new QuestionVote { QuestionId = questionId });
        }

        // POST /api/question-bank/{questionId}/vote  { "isLike": true }
        [HttpPost("question-bank/{questionId:guid}/vote")]
        public async Task<ActionResult<QuestionVoteResponseDto>> VoteOnQuestionBankQuestion(Guid questionId, QuestionVoteRequestDto dto)
        {
            if (!await _db.QuestionBankQuestions.AnyAsync(q => q.Id == questionId && q.IsActive))
                return NotFound(new { message = "Question not found." });

            return await ApplyVoteAsync(dto.IsLike, v => v.QuestionBankQuestionId == questionId, () => new QuestionVote { QuestionBankQuestionId = questionId });
        }

        private async Task<ActionResult<QuestionVoteResponseDto>> ApplyVoteAsync(
            bool isLike,
            System.Linq.Expressions.Expression<Func<QuestionVote, bool>> matchesQuestion,
            Func<QuestionVote> makeNewVote)
        {
            var userId = User.GetUserId();

            // matchesQuestion narrows to the one question; combined with UserId this finds at most
            // one row given the unique indexes in ScoramDbContext.
            var existing = await _db.QuestionVotes.Where(matchesQuestion).FirstOrDefaultAsync(v => v.UserId == userId);

            if (existing == null)
            {
                var vote = makeNewVote();
                vote.UserId = userId;
                vote.IsLike = isLike;
                _db.QuestionVotes.Add(vote);
            }
            else if (existing.IsLike == isLike)
            {
                // Clicking the same reaction again retracts it.
                _db.QuestionVotes.Remove(existing);
            }
            else
            {
                existing.IsLike = isLike;
            }

            await _db.SaveChangesAsync();

            var likeCount = await _db.QuestionVotes.Where(matchesQuestion).CountAsync(v => v.IsLike);
            var dislikeCount = await _db.QuestionVotes.Where(matchesQuestion).CountAsync(v => !v.IsLike);
            var myVote = await _db.QuestionVotes.Where(matchesQuestion).Where(v => v.UserId == userId).Select(v => (bool?)v.IsLike).FirstOrDefaultAsync();

            return Ok(new QuestionVoteResponseDto { LikeCount = likeCount, DislikeCount = dislikeCount, MyVote = myVote });
        }
    }
}
