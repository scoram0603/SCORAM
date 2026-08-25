using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;

namespace ScoramAPI.Controllers
{
    // LANDING PAGE -- powers the hero/stats section on the new public marketing page
    // (ScoramWeb/src/pages/Landing.jsx). Deliberately a brand-new, tiny, anonymous, read-only
    // controller rather than reusing DashboardController's "api/admin/dashboard/stats" -- that one
    // is Admin/SuperAdmin-gated and returns internal operational data (audit log, admin performance,
    // report queues) that must never be public. This exposes only three honest aggregate counts, the
    // same numbers a student could work out by scrolling the app themselves. No admin panel, admin
    // API, or admin business logic is touched by this file.
    [ApiController]
    [Route("api/public-stats")]
    public class PublicStatsController : ControllerBase
    {
        private readonly ScoramDbContext _db;

        public PublicStatsController(ScoramDbContext db)
        {
            _db = db;
        }

        // GET /api/public-stats -- anonymous. Real counts only, never fabricated: PYQ-paper
        // questions + Question Bank questions, published/unblocked exams, and active registered
        // students. Kept as one cheap round trip since this loads on every visit to "/".
        [HttpGet]
        public async Task<ActionResult<PublicStatsDto>> Get()
        {
            var paperQuestions = await _db.Questions.CountAsync();
            var bankQuestions = await _db.QuestionBankQuestions.CountAsync();
            var exams = await _db.Exams.CountAsync(e => !e.IsBlocked);
            var students = await _db.Users.CountAsync(u => u.IsActive);

            return Ok(new PublicStatsDto
            {
                TotalQuestions = paperQuestions + bankQuestions,
                TotalExams = exams,
                TotalStudents = students
            });
        }
    }
}
