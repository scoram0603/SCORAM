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

            var users = await _db.Users
                .Where(u => u.IsActive && u.Id != userId)
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
    }
}
