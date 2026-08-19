using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Extensions;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    [ApiController]
    [Route("api/referrals")]
    [Authorize(Roles = "Student")]
    public class ReferralsController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly IGamificationService _gamification;

        public ReferralsController(ScoramDbContext db, IGamificationService gamification)
        {
            _db = db;
            _gamification = gamification;
        }

        // GET /api/referrals/me -- code is generated on first call if the student doesn't have one
        // yet (see GamificationService.GetOrCreateReferralCodeAsync), so this is safe to call as soon
        // as the Referral tab opens without a separate "generate" step.
        [HttpGet("me")]
        public async Task<ActionResult<ReferralSummaryDto>> Me()
        {
            var userId = User.GetUserId();
            var code = await _gamification.GetOrCreateReferralCodeAsync(userId);

            var referrals = await _db.Referrals
                .Where(r => r.ReferrerUserId == userId && r.ReferredUserId != null)
                .Include(r => r.ReferredUser)
                .OrderByDescending(r => r.JoinedAt)
                .ToListAsync();

            var bonusAttempts = await _db.Users.Where(u => u.Id == userId).Select(u => u.BonusMockAttempts).FirstOrDefaultAsync();

            return Ok(new ReferralSummaryDto
            {
                ReferralCode = code,
                ShareText = $"Join me on Scoram and level up your exam prep! Use my code {code} when you sign up.",
                TotalJoins = referrals.Count,
                TotalXpEarned = referrals.Count * GamificationService.ReferralXpReward,
                BonusMockAttempts = bonusAttempts,
                Referrals = referrals.Select(r => new ReferralItemDto
                {
                    ReferredFullName = r.ReferredUser?.FullName ?? "A student",
                    JoinedAt = r.JoinedAt
                }).ToList()
            });
        }
    }
}
