using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Extensions;

namespace ScoramAPI.Controllers
{
    [ApiController]
    [Route("api/users")]
    [Authorize(Roles = "Student")]
    public class UsersController : ControllerBase
    {
        private readonly ScoramDbContext _db;

        public UsersController(ScoramDbContext db)
        {
            _db = db;
        }

        // GET /api/users/search?q=some_username -- powers "start a new conversation" search on the
        // Messages tab. Matches by username (the app's de facto "ID" a student shares with a friend)
        // or full name, case-insensitive, excludes the current user and deactivated accounts.
        [HttpGet("search")]
        public async Task<ActionResult<List<UserSearchResultDto>>> Search([FromQuery] string q)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
                return Ok(new List<UserSearchResultDto>());

            var userId = User.GetUserId();
            var normalized = q.Trim().ToLowerInvariant();

            // Never surface students who blocked this user, or whom this user blocked.
            var blockedIds = _db.UserBlocks
                .Where(b => b.BlockerUserId == userId || b.BlockedUserId == userId)
                .Select(b => b.BlockerUserId == userId ? b.BlockedUserId : b.BlockerUserId);

            var users = await _db.Users
                .Where(u => u.IsActive && u.Id != userId && !blockedIds.Contains(u.Id))
                .Where(u => u.Username.Contains(normalized) || u.FullName.ToLower().Contains(normalized))
                .OrderBy(u => u.Username)
                .Take(20)
                .Select(u => new UserSearchResultDto
                {
                    Id = u.Id,
                    Username = u.Username,
                    FullName = u.FullName,
                    PhotoUrl = u.PhotoUrl
                })
                .ToListAsync();

            return Ok(users);
        }

        // GET /api/users/mention-search?q= -- @mention autocomplete for discussion comments/replies.
        // Unlike Search above (2-char minimum, for "start a conversation"), this answers for an empty or
        // 1-letter query so suggestions can open the instant "@" is typed: an empty query returns the most
        // recently active students, otherwise username-prefix matches rank ahead of other matches.
        [HttpGet("mention-search")]
        public async Task<ActionResult<List<UserSearchResultDto>>> MentionSearch([FromQuery] string? q)
        {
            var userId = User.GetUserId();
            var normalized = (q ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized.Length > 30) return Ok(new List<UserSearchResultDto>());

            var blockedIds = _db.UserBlocks
                .Where(b => b.BlockerUserId == userId || b.BlockedUserId == userId)
                .Select(b => b.BlockerUserId == userId ? b.BlockedUserId : b.BlockerUserId);

            var query = _db.Users.AsNoTracking()
                .Where(u => u.IsActive && u.Id != userId && !blockedIds.Contains(u.Id));

            IOrderedQueryable<ScoramAPI.Models.User> ordered;
            if (normalized.Length == 0)
            {
                ordered = query.OrderByDescending(u => u.LastActiveAt);
            }
            else
            {
                query = query.Where(u => u.Username.Contains(normalized) || u.FullName.ToLower().Contains(normalized));
                ordered = query.OrderBy(u => u.Username.StartsWith(normalized) ? 0 : 1).ThenBy(u => u.Username);
            }

            var users = await ordered
                .Take(8)
                .Select(u => new UserSearchResultDto { Id = u.Id, Username = u.Username, FullName = u.FullName, PhotoUrl = u.PhotoUrl })
                .ToListAsync();

            return Ok(users);
        }
    }
}
