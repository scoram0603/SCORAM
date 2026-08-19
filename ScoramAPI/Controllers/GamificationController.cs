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
    [ApiController]
    [Route("api/gamification")]
    [Authorize(Roles = "Student")]
    public class GamificationController : ControllerBase
    {
        private const int LeaderboardTopN = 20;

        private readonly ScoramDbContext _db;

        public GamificationController(ScoramDbContext db)
        {
            _db = db;
        }

        // GET /api/gamification/me
        [HttpGet("me")]
        public async Task<ActionResult<GamificationSummaryDto>> Me()
        {
            var userId = User.GetUserId();

            var xp = await _db.UserXPs.FirstOrDefaultAsync(x => x.UserId == userId);
            var streak = await _db.UserStreaks.FirstOrDefaultAsync(s => s.UserId == userId);
            if (xp == null || streak == null) return NotFound(new { message = "Gamification profile not found." });

            var badgeCount = await _db.UserBadges.CountAsync(b => b.UserId == userId);
            var bonusAttempts = await _db.Users.Where(u => u.Id == userId).Select(u => u.BonusMockAttempts).FirstOrDefaultAsync();

            var (nextLevel, xpToNext) = NextLevelInfo(xp.CurrentLevel, xp.TotalXP);

            return Ok(new GamificationSummaryDto
            {
                CurrentStreak = streak.CurrentStreak,
                LongestStreak = streak.LongestStreak,
                FreezesUsedThisWeek = streak.FreezesUsedThisWeek,
                FreezesAvailableThisWeek = Math.Max(0, 1 - streak.FreezesUsedThisWeek), // MaxFreezesPerWeek == 1, see GamificationService
                LastActiveDate = streak.LastActiveDate,
                TotalXP = xp.TotalXP,
                CurrentLevel = xp.CurrentLevel.ToString(),
                NextLevel = nextLevel,
                XpToNextLevel = xpToNext,
                BadgeCount = badgeCount,
                BonusMockAttempts = bonusAttempts
            });
        }

        // GET /api/gamification/badges -- full master list, flagged per-student
        [HttpGet("badges")]
        public async Task<ActionResult<List<BadgeDto>>> Badges()
        {
            var userId = User.GetUserId();

            var earned = await _db.UserBadges
                .Where(ub => ub.UserId == userId)
                .ToDictionaryAsync(ub => ub.BadgeId, ub => ub.EarnedAt);

            var badges = await _db.Badges.OrderBy(b => b.Name).ToListAsync();

            return Ok(badges.Select(b => new BadgeDto
            {
                Id = b.Id,
                Name = b.Name,
                Description = b.Description ?? string.Empty,
                IconUrl = b.IconUrl,
                CriteriaDescription = b.CriteriaDescription ?? string.Empty,
                Earned = earned.ContainsKey(b.Id),
                EarnedAt = earned.TryGetValue(b.Id, out var at) ? at : null
            }).ToList());
        }

        // GET /api/gamification/leaderboard?scope=global|exam|friends&period=alltime|weekly|monthly&examName=
        //
        // Computed live from XpTransactions/UserXP rather than a precomputed LeaderboardCache table --
        // at this app's current scale a direct GROUP BY is fast and always exactly correct, and it
        // avoids standing up a background refresh job for a cache that would just be a performance
        // optimization for a scale we're not at yet. Worth revisiting if/when the student count grows
        // enough for this to show up as a real cost.
        [HttpGet("leaderboard")]
        public async Task<ActionResult<LeaderboardResponseDto>> Leaderboard(
            [FromQuery] string scope = "global",
            [FromQuery] string period = "alltime",
            [FromQuery] string? examName = null)
        {
            var userId = User.GetUserId();
            scope = scope.ToLowerInvariant();
            period = period.ToLowerInvariant();

            if (scope == "exam" && string.IsNullOrWhiteSpace(examName))
                return BadRequest(new { message = "examName is required when scope=exam." });

            List<(Guid UserId, int Xp)> ranked;

            if (scope == "friends")
            {
                // "Friends" == people this student referred, plus themselves. All-time XP only --
                // this circle is small enough that a period filter wouldn't mean much.
                var friendIds = await _db.Referrals
                    .Where(r => r.ReferrerUserId == userId && r.ReferredUserId != null)
                    .Select(r => r.ReferredUserId!.Value)
                    .ToListAsync();
                friendIds.Add(userId);

                var raw = await _db.UserXPs
                    .Where(x => friendIds.Contains(x.UserId))
                    .Select(x => new { x.UserId, x.TotalXP })
                    .ToListAsync();
                ranked = raw.Select(x => (x.UserId, x.TotalXP)).ToList();
            }
            else if (period == "alltime" && scope == "global")
            {
                // All-time global is just UserXP.TotalXP directly -- no need to sum the transaction
                // log for the one case that already has a running total maintained.
                var raw = await _db.UserXPs
                    .OrderByDescending(x => x.TotalXP)
                    .Select(x => new { x.UserId, x.TotalXP })
                    .ToListAsync();
                ranked = raw.Select(x => (x.UserId, x.TotalXP)).ToList();
            }
            else
            {
                var query = _db.XpTransactions.AsQueryable();

                if (period == "weekly")
                {
                    var weekStart = DateTime.UtcNow.Date.AddDays(-7);
                    query = query.Where(t => t.CreatedAt >= weekStart);
                }
                else if (period == "monthly")
                {
                    var monthStart = DateTime.UtcNow.Date.AddDays(-30);
                    query = query.Where(t => t.CreatedAt >= monthStart);
                }

                if (scope == "exam")
                    query = query.Where(t => t.ExamName == examName);

                var raw = await query
                    .GroupBy(t => t.UserId)
                    .Select(g => new { UserId = g.Key, Xp = g.Sum(t => t.Amount) })
                    .ToListAsync();
                ranked = raw.Select(x => (x.UserId, x.Xp)).ToList();
            }

            ranked = ranked.OrderByDescending(r => r.Xp).ToList();

            var topRows = ranked.Take(LeaderboardTopN).ToList();
            var callerInTop = topRows.Any(r => r.UserId == userId);
            var rowsToShow = topRows;
            int? callerRank = null;

            if (!callerInTop)
            {
                var idx = ranked.FindIndex(r => r.UserId == userId);
                if (idx >= 0)
                {
                    callerRank = idx + 1;
                    rowsToShow = topRows.Append(ranked[idx]).ToList();
                }
            }

            var userIds = rowsToShow.Select(r => r.UserId).Distinct().ToList();
            var userInfo = await _db.Users
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Username, u.FullName, u.PhotoUrl })
                .ToDictionaryAsync(u => u.Id);

            var entries = new List<LeaderboardEntryDto>();
            for (int i = 0; i < topRows.Count; i++)
            {
                var row = topRows[i];
                if (!userInfo.TryGetValue(row.UserId, out var info)) continue;
                entries.Add(new LeaderboardEntryDto
                {
                    Rank = i + 1,
                    UserId = row.UserId,
                    Username = info.Username,
                    FullName = info.FullName,
                    PhotoUrl = info.PhotoUrl,
                    Xp = row.Xp,
                    IsCurrentUser = row.UserId == userId
                });
            }

            if (!callerInTop && callerRank.HasValue)
            {
                var callerRow = ranked[callerRank.Value - 1];
                if (userInfo.TryGetValue(callerRow.UserId, out var info))
                {
                    entries.Add(new LeaderboardEntryDto
                    {
                        Rank = callerRank.Value,
                        UserId = callerRow.UserId,
                        Username = info.Username,
                        FullName = info.FullName,
                        PhotoUrl = info.PhotoUrl,
                        Xp = callerRow.Xp,
                        IsCurrentUser = true
                    });
                }
            }

            return Ok(new LeaderboardResponseDto
            {
                Scope = scope,
                Period = period,
                ExamName = scope == "exam" ? examName : null,
                Entries = entries
            });
        }

        private static (string? NextLevel, int? XpToNext) NextLevelInfo(UserLevel current, int totalXp)
        {
            return current switch
            {
                UserLevel.Beginner => ("Intermediate", 500 - totalXp),
                UserLevel.Intermediate => ("Expert", 2000 - totalXp),
                UserLevel.Expert => ("Master", 5000 - totalXp),
                _ => (null, null)
            };
        }
    }
}
