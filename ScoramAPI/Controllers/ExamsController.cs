using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Models;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // Step 1 of the admin PYQ upload wizard: "choose exam you've already created (SSC CGL) or
    // + New Exam (enter name, choose logo)". This list also doubles as a public "browse by exam"
    // list for students, which is why GET is anonymous but creating an exam is Admin-only.
    [ApiController]
    [Route("api/exams")]
    public class ExamsController : ControllerBase
    {
        private static readonly string[] AllowedLogoExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".svg" };
        private const long MaxLogoSizeBytes = 2 * 1024 * 1024; // 2 MB

        private readonly ScoramDbContext _db;
        private readonly IFileStorageService _fileStorage;
        private readonly IAdminPermissionService _permissions;
        private readonly IAuditLogService _audit;

        public ExamsController(
            ScoramDbContext db, IFileStorageService fileStorage,
            IAdminPermissionService permissions, IAuditLogService audit)
        {
            _db = db;
            _fileStorage = fileStorage;
            _permissions = permissions;
            _audit = audit;
        }

        // GET /api/exams -- the picker list (also usable as a public "browse by exam" list). Excludes
        // blocked exams -- see IsBlocked -- for non-admin callers; GET /api/admin/exams below is the
        // unfiltered version admins manage from.
        [HttpGet]
        public async Task<ActionResult<List<ExamResponseDto>>> List([FromQuery] Guid? organizationId)
        {
            // ORGANIZATION HIERARCHY -- a blocked Organization hides every exam under it from this
            // public list too, without touching each exam's own IsBlocked flag (see
            // Organization.IsBlocked's own comment). organizationId powers the two-step "pick an
            // Organization, then pick from its exams" picker everywhere one exists.
            var query = _db.Exams
                .Where(e => !e.IsBlocked && (e.Organization == null || !e.Organization.IsBlocked));
            if (organizationId.HasValue) query = query.Where(e => e.OrganizationId == organizationId.Value);

            var exams = await query
                .OrderBy(e => e.Name)
                .Select(e => new ExamResponseDto
                {
                    Id = e.Id,
                    Name = e.Name,
                    LogoUrl = e.LogoUrl,
                    IsBlocked = e.IsBlocked,
                    // PYP half only here -- the PYQ (Question Bank) half is merged in below via
                    // GetQuestionBankCountsByExamAsync, see that method's own comment for why.
                    QuestionCount = e.Questions.Count + e.Papers.SelectMany(p => p.Questions).Count(),
                    CreatedAt = e.CreatedAt,
                    OrganizationId = e.OrganizationId,
                    OrganizationName = e.Organization != null ? e.Organization.Name : null
                })
                .ToListAsync();

            var bankCounts = await GetQuestionBankCountsByExamAsync(_db);
            foreach (var exam in exams)
                exam.QuestionCount += bankCounts.GetValueOrDefault(exam.Id);

            return Ok(exams);
        }

        // GET /api/admin/exams  (Admin only) -- same shape as above but includes blocked exams (and
        // exams under a blocked Organization) too, for the admin "Manage Exams" screen.
        [HttpGet("/api/admin/exams")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<ActionResult<List<ExamResponseDto>>> AdminList([FromQuery] Guid? organizationId)
        {
            var query = _db.Exams.AsQueryable();
            if (organizationId.HasValue) query = query.Where(e => e.OrganizationId == organizationId.Value);

            var exams = await query
                .OrderBy(e => e.Name)
                .Select(e => new ExamResponseDto
                {
                    Id = e.Id,
                    Name = e.Name,
                    LogoUrl = e.LogoUrl,
                    IsBlocked = e.IsBlocked,
                    // PYP half only here -- the PYQ (Question Bank) half is merged in below via
                    // GetQuestionBankCountsByExamAsync, see that method's own comment for why.
                    QuestionCount = e.Questions.Count + e.Papers.SelectMany(p => p.Questions).Count(),
                    CreatedAt = e.CreatedAt,
                    OrganizationId = e.OrganizationId,
                    OrganizationName = e.Organization != null ? e.Organization.Name : null
                })
                .ToListAsync();

            var bankCounts = await GetQuestionBankCountsByExamAsync(_db);
            foreach (var exam in exams)
                exam.QuestionCount += bankCounts.GetValueOrDefault(exam.Id);

            return Ok(exams);
        }

        // POST /api/admin/exams  (Admin only) -- "+ New Exam": Enter Exam Name, Choose Exam Logo
        [HttpPost("/api/admin/exams")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        [RequestSizeLimit(MaxLogoSizeBytes + 1024)]
        public async Task<ActionResult<ExamResponseDto>> Create([FromForm] ExamCreateDto dto)
        {
            var name = dto.Name.Trim();
            if (await _db.Exams.AnyAsync(e => e.Name.ToLower() == name.ToLower()))
                return Conflict(new { message = $"An exam named \"{name}\" already exists -- pick it from the list instead of creating a duplicate." });

            // ORGANIZATION HIERARCHY -- optional; validated up front so a typo'd/deleted
            // OrganizationId fails loudly here rather than silently creating an orphaned FK.
            Organization? organization = null;
            if (dto.OrganizationId.HasValue)
            {
                organization = await _db.Organizations.FirstOrDefaultAsync(o => o.Id == dto.OrganizationId.Value);
                if (organization == null) return BadRequest(new { message = "Selected organization could not be found." });
            }

            string? logoUrl = null;
            if (dto.Logo != null)
            {
                var validationError = ValidateLogo(dto.Logo);
                if (validationError != null) return BadRequest(new { message = validationError });

                logoUrl = await _fileStorage.SaveImageAsync(dto.Logo, "exam-logos");
            }

            var exam = new Exam
            {
                Name = name,
                LogoUrl = logoUrl,
                OrganizationId = organization?.Id,
                CreatedByAdminId = User.GetAdminId(),
                CreatedAt = DateTime.UtcNow
            };

            _db.Exams.Add(exam);

            // Every exam gets a chat room automatically -- joining it is optional (see
            // ChatRoomsController), this just makes sure the room exists to be joined.
            // GROUP CHAT FIX -- IsFeatured = false: a student finds this room by searching for the
            // exam by name (ChatController.ListRooms), rather than every exam ever created flooding
            // everyone's default room list. See ChatRoom.IsFeatured for the full reasoning.
            _db.ChatRooms.Add(new ChatRoom
            {
                ExamId = exam.Id,
                Name = exam.Name,
                Description = $"Discussion room for {exam.Name} aspirants",
                IsFeatured = false,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            return Ok(new ExamResponseDto
            {
                Id = exam.Id,
                Name = exam.Name,
                LogoUrl = exam.LogoUrl,
                IsBlocked = false,
                QuestionCount = 0,
                CreatedAt = exam.CreatedAt,
                OrganizationId = organization?.Id,
                OrganizationName = organization?.Name
            });
        }

        // PATCH /api/admin/exams/{id}  (Admin only) -- rename and/or replace the logo. Renaming also
        // keeps the linked chat room's denormalized Name in sync (see ChatRoom.Name) since students
        // find that room by searching for the exam name (ChatController.ListRooms).
        [HttpPatch("/api/admin/exams/{id:guid}")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        [RequestSizeLimit(MaxLogoSizeBytes + 1024)]
        public async Task<ActionResult<ExamResponseDto>> Update(Guid id, [FromForm] ExamUpdateDto dto)
        {
            var exam = await _db.Exams.FirstOrDefaultAsync(e => e.Id == id);
            if (exam == null) return NotFound();

            if (dto.Name != null)
            {
                var name = dto.Name.Trim();
                if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { message = "Name can't be empty." });
                var conflictingExam = await _db.Exams.FirstOrDefaultAsync(e => e.Id != id && e.Name.ToLower() == name.ToLower());
                if (conflictingExam != null)
                    // conflictingExamId lets the frontend offer "merge into it?" instead of just
                    // showing this as a dead-end error -- see MergeInto below for what that means.
                    return Conflict(new { message = $"An exam named \"{name}\" already exists.", conflictingExamId = conflictingExam.Id });

                exam.Name = name;

                var room = await _db.ChatRooms.FirstOrDefaultAsync(r => r.ExamId == id);
                if (room != null) room.Name = name;
            }

            if (dto.Logo != null)
            {
                var validationError = ValidateLogo(dto.Logo);
                if (validationError != null) return BadRequest(new { message = validationError });

                var oldLogoUrl = exam.LogoUrl;
                exam.LogoUrl = await _fileStorage.SaveImageAsync(dto.Logo, "exam-logos");
                await _fileStorage.DeleteImageAsync(oldLogoUrl);
            }

            // ORGANIZATION HIERARCHY -- see ExamUpdateDto.ClearOrganization's own comment on why
            // clearing needs its own explicit flag rather than just sending a null OrganizationId.
            if (dto.ClearOrganization)
            {
                exam.OrganizationId = null;
            }
            else if (dto.OrganizationId.HasValue)
            {
                var organizationExists = await _db.Organizations.AnyAsync(o => o.Id == dto.OrganizationId.Value);
                if (!organizationExists) return BadRequest(new { message = "Selected organization could not be found." });
                exam.OrganizationId = dto.OrganizationId.Value;
            }

            await _db.SaveChangesAsync();

            var updatedOrgName = exam.OrganizationId.HasValue
                ? await _db.Organizations.Where(o => o.Id == exam.OrganizationId).Select(o => o.Name).FirstOrDefaultAsync()
                : null;

            return Ok(new ExamResponseDto
            {
                Id = exam.Id,
                Name = exam.Name,
                LogoUrl = exam.LogoUrl,
                IsBlocked = exam.IsBlocked,
                QuestionCount = await GetCombinedQuestionCountAsync(_db, id),
                CreatedAt = exam.CreatedAt,
                OrganizationId = exam.OrganizationId,
                OrganizationName = updatedOrgName
            });
        }

        // POST /api/admin/exams/{sourceId}/merge-into/{targetId}  (same Admin/SuperAdmin gate as
        // Update above) -- reached from Manage Exam when a rename collides with an exam that already
        // has that name (see Update's own conflictingExamId). Moves every real thing on sourceId onto
        // targetId, then deletes sourceId: all Papers and legacy standalone Questions (a straight
        // ExamId reassignment -- neither has any exam-scoped uniqueness constraint that this could
        // violate), every QuestionBankExamMapping (reassigned unless the same question is already
        // mapped to targetId under the same Year, in which case sourceId's copy is just dropped as
        // redundant rather than colliding with the (QuestionBankQuestionId, ExamId, Year) unique
        // index). targetId's own Organization/Logo/IsBlocked/Name are left completely untouched --
        // it's the surviving exam, sourceId is the one being absorbed. sourceId's chat room is
        // deleted without migrating its messages (a deliberate choice, not an oversight -- merging
        // chat history was explicitly ruled out in favor of simplicity); targetId's own chat room is
        // untouched. Runs in one transaction, same as DeleteCascade.
        [HttpPost("/api/admin/exams/{sourceId:guid}/merge-into/{targetId:guid}")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<ActionResult<ExamResponseDto>> MergeInto(Guid sourceId, Guid targetId)
        {
            if (sourceId == targetId) return BadRequest(new { message = "Can't merge an exam into itself." });

            var source = await _db.Exams.FirstOrDefaultAsync(e => e.Id == sourceId);
            var target = await _db.Exams.FirstOrDefaultAsync(e => e.Id == targetId);
            if (source == null || target == null) return NotFound();

            await using var transaction = await _db.Database.BeginTransactionAsync();

            var papers = await _db.Papers.Where(p => p.ExamId == sourceId).ToListAsync();
            foreach (var p in papers) p.ExamId = targetId;

            var legacyQuestions = await _db.Questions.Where(q => q.ExamId == sourceId).ToListAsync();
            foreach (var q in legacyQuestions) q.ExamId = targetId;

            // MockTest.ExamId / PracticeTestTemplate.ExamId -- neither has any exam-scoped uniqueness
            // constraint (unlike the mapping/preference tables below), so a straight reassignment is
            // enough; both are Restrict FKs (see ScoramDbContext), so skipping this would make the
            // final _db.Exams.Remove(source) below fail instead of silently losing anything.
            var mockTests = await _db.MockTests.Where(m => m.ExamId == sourceId).ToListAsync();
            foreach (var m in mockTests) m.ExamId = targetId;

            var practiceTemplates = await _db.PracticeTestTemplates.Where(t => t.ExamId == sourceId).ToListAsync();
            foreach (var t in practiceTemplates) t.ExamId = targetId;

            var sourceMappings = await _db.QuestionBankExamMappings.Where(m => m.ExamId == sourceId).ToListAsync();
            var targetPairs = await _db.QuestionBankExamMappings
                .Where(m => m.ExamId == targetId)
                .Select(m => new { m.QuestionBankQuestionId, m.Year })
                .ToListAsync();
            var targetPairSet = targetPairs.Select(x => (x.QuestionBankQuestionId, x.Year)).ToHashSet();

            foreach (var m in sourceMappings)
            {
                if (targetPairSet.Contains((m.QuestionBankQuestionId, m.Year)))
                    _db.QuestionBankExamMappings.Remove(m); // already tagged to target for this year -- source's copy is redundant
                else
                    m.ExamId = targetId;
            }

            // UserExamPreference -- (UserId, ExamId) is unique (a student can't have the same exam
            // twice in "My Exams"), so a student who already has BOTH source and target exams saved
            // would collide on a straight reassignment; source's row is dropped as redundant instead,
            // same idea as the mapping dedup above. IsPrimary is preserved across the drop -- if the
            // row being dropped was this student's Primary Exam and the surviving (target) row isn't,
            // the flag is carried over rather than just disappearing (at most one of the two rows can
            // already be Primary -- see the UserId-where-IsPrimary=1 filtered unique index).
            var sourcePrefs = await _db.UserExamPreferences.Where(p => p.ExamId == sourceId).ToListAsync();
            var targetPrefsByUser = await _db.UserExamPreferences
                .Where(p => p.ExamId == targetId)
                .ToDictionaryAsync(p => p.UserId);

            foreach (var pref in sourcePrefs)
            {
                if (targetPrefsByUser.TryGetValue(pref.UserId, out var existingTargetPref))
                {
                    if (pref.IsPrimary && !existingTargetPref.IsPrimary) existingTargetPref.IsPrimary = true;
                    _db.UserExamPreferences.Remove(pref);
                }
                else
                {
                    pref.ExamId = targetId;
                }
            }

            var sourceRoom = await _db.ChatRooms.FirstOrDefaultAsync(r => r.ExamId == sourceId);
            if (sourceRoom != null) _db.ChatRooms.Remove(sourceRoom); // chat history intentionally not migrated -- see this method's own comment

            _db.Exams.Remove(source);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            await _audit.LogAsync(User.GetAdminId(), "Exam.MergeInto", "Exam", targetId,
                $"Merged \"{source.Name}\" into \"{target.Name}\": {papers.Count} paper(s), {legacyQuestions.Count} legacy question(s), " +
                $"{mockTests.Count} mock test(s), {practiceTemplates.Count} practice template(s), " +
                $"{sourceMappings.Count} PYQ mapping(s) and {sourcePrefs.Count} student preference(s) carried over or deduplicated");

            var targetOrgName = target.OrganizationId.HasValue
                ? await _db.Organizations.Where(o => o.Id == target.OrganizationId).Select(o => o.Name).FirstOrDefaultAsync()
                : null;

            return Ok(new ExamResponseDto
            {
                Id = target.Id,
                Name = target.Name,
                LogoUrl = target.LogoUrl,
                IsBlocked = target.IsBlocked,
                QuestionCount = await GetCombinedQuestionCountAsync(_db, targetId),
                CreatedAt = target.CreatedAt,
                OrganizationId = target.OrganizationId,
                OrganizationName = targetOrgName
            });
        }

        // PATCH /api/admin/exams/{id}/block  (Admin only) -- hide/unhide, see Exam.IsBlocked. Blocking
        // also disables (but doesn't delete) the linked chat room, matching "no new engagement" intent.
        [HttpPatch("/api/admin/exams/{id:guid}/block")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<ActionResult<ExamResponseDto>> SetBlocked(Guid id, [FromBody] ExamBlockDto dto)
        {
            var exam = await _db.Exams.FirstOrDefaultAsync(e => e.Id == id);
            if (exam == null) return NotFound();

            exam.IsBlocked = dto.IsBlocked;

            var room = await _db.ChatRooms.FirstOrDefaultAsync(r => r.ExamId == id);
            if (room != null) room.IsChatDisabled = dto.IsBlocked;

            await _db.SaveChangesAsync();

            var orgName = exam.OrganizationId.HasValue
                ? await _db.Organizations.Where(o => o.Id == exam.OrganizationId).Select(o => o.Name).FirstOrDefaultAsync()
                : null;

            return Ok(new ExamResponseDto
            {
                Id = exam.Id,
                Name = exam.Name,
                LogoUrl = exam.LogoUrl,
                IsBlocked = exam.IsBlocked,
                QuestionCount = await GetCombinedQuestionCountAsync(_db, id),
                CreatedAt = exam.CreatedAt,
                OrganizationId = exam.OrganizationId,
                OrganizationName = orgName
            });
        }

        // DELETE /api/admin/exams/{id}  (SuperAdmin only -- this is destructive and, unlike Block,
        // can't be undone) -- only succeeds if the exam is genuinely empty: no PYQ questions, no
        // Question Bank mappings, no Practice Test templates, no Mock Tests, and its chat room (if
        // any) has no messages/members. Anything else and the answer is Block, not Delete -- returns
        // 409 saying so rather than silently orphaning or cascading through a student's real activity.
        [HttpDelete("/api/admin/exams/{id:guid}")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var exam = await _db.Exams.FirstOrDefaultAsync(e => e.Id == id);
            if (exam == null) return NotFound();

            if (await ExamHasContentAsync(_db, id, exam.Name))
                return Conflict(new { message = "This exam has questions, tests, or chat activity attached -- Block it instead of deleting." });

            var room = await _db.ChatRooms.FirstOrDefaultAsync(r => r.ExamId == id);
            if (room != null) _db.ChatRooms.Remove(room);
            _db.Exams.Remove(exam);
            await _db.SaveChangesAsync();

            return NoContent();
        }

        // GET /api/admin/exams/{id}/delete-options  (SuperAdmin only) -- everything the "Delete this
        // exam" screen needs before it can even show the warning: every Paper under the exam (with
        // its full identity -- Year/Tier/Shift/Date/Language/PaperCode -- and its own question
        // count) for the "which papers?" selection, every distinct PYQ Year this exam has Question
        // Bank content under (Question Bank only carries Year, not Tier/Shift/Date -- see
        // QuestionBankExamMapping), and an aggregate count of real student activity a force-delete
        // would remove (answers, bookmarks, mock-test/quiz/practice-test entries, comments, votes,
        // reports) for the strict warning. See DeleteCascade below for the actual delete.
        [HttpGet("/api/admin/exams/{id:guid}/delete-options")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult<ExamDeleteOptionsDto>> GetDeleteOptions(Guid id)
        {
            var exam = await _db.Exams.FirstOrDefaultAsync(e => e.Id == id);
            if (exam == null) return NotFound();

            var paperEntities = await _db.Papers.Where(p => p.ExamId == id).ToListAsync();
            var paperIds = paperEntities.Select(p => p.Id).ToList();
            var ownQuestionCountsByPaper = await _db.Questions
                .Where(q => q.PaperId != null && paperIds.Contains(q.PaperId.Value))
                .GroupBy(q => q.PaperId!.Value)
                .Select(g => new { PaperId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.PaperId, x => x.Count);
            // A Paper's full question list is its own Questions UNION its PaperQuestionBankLink rows
            // (existing Question Bank questions mapped onto it -- "Previous Year Paper Practice", see
            // PaperQuestionBankLink's own comment); both need to be counted for the total shown here
            // to match what actually gets removed when the paper does.
            var linkedQuestionCountsByPaper = await _db.PaperQuestionBankLinks
                .Where(l => paperIds.Contains(l.PaperId))
                .GroupBy(l => l.PaperId)
                .Select(g => new { PaperId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.PaperId, x => x.Count);

            // Materialized (not projected inside the SQL query) -- Language/Status are enums and
            // .ToString() on them doesn't reliably translate to SQL, so mapping happens in memory
            // after the entities are loaded, same pattern used everywhere else a DTO needs an enum's
            // string form.
            var papers = paperEntities
                .OrderByDescending(p => p.Year)
                .Select(p => new ExamDeletePaperOptionDto
                {
                    PaperId = p.Id,
                    Year = p.Year,
                    Tier = p.Tier,
                    ExamDate = p.ExamDate,
                    Shift = p.Shift,
                    PaperLabel = p.PaperLabel,
                    PaperCode = p.PaperCode,
                    Language = p.Language.ToString(),
                    Status = p.Status.ToString(),
                    QuestionCount = (ownQuestionCountsByPaper.TryGetValue(p.Id, out var ownCount) ? ownCount : 0)
                        + (linkedQuestionCountsByPaper.TryGetValue(p.Id, out var linkedCount) ? linkedCount : 0)
                })
                .ToList();

            var pyqYears = await _db.QuestionBankExamMappings
                .Where(m => m.ExamId == id)
                .GroupBy(m => m.Year)
                .Select(g => new ExamDeletePyqYearOptionDto
                {
                    Year = g.Key,
                    QuestionCount = g.Select(m => m.QuestionBankQuestionId).Distinct().Count()
                })
                .OrderByDescending(y => y.Year)
                .ToListAsync();

            var legacyQuestionCount = await _db.Questions.CountAsync(q => q.ExamId == id);

            // Usage counts span BOTH sides of what a force-delete would remove -- the legacy Question
            // side (this exam's Papers, plus any pre-Paper standalone Questions) and the Question
            // Bank side (this exam's mapped questions).
            var pypQuestionIds = await _db.Questions
                .Where(q => (q.PaperId != null && q.Paper!.ExamId == id) || q.ExamId == id)
                .Select(q => q.Id)
                .ToListAsync();
            var pyqQuestionIds = await _db.QuestionBankExamMappings
                .Where(m => m.ExamId == id)
                .Select(m => m.QuestionBankQuestionId)
                .Distinct()
                .ToListAsync();

            var usage = new ExamDeleteUsageCountsDto
            {
                StudentAnswers = await _db.StudentAnswers.CountAsync(a =>
                    (a.QuestionId != null && pypQuestionIds.Contains(a.QuestionId.Value)) ||
                    (a.QuestionBankQuestionId != null && pyqQuestionIds.Contains(a.QuestionBankQuestionId.Value))),
                Bookmarks = await _db.Bookmarks.CountAsync(b =>
                    (b.QuestionId != null && pypQuestionIds.Contains(b.QuestionId.Value)) ||
                    (b.QuestionBankQuestionId != null && pyqQuestionIds.Contains(b.QuestionBankQuestionId.Value))),
                MockTestUsages = await _db.MockTestQuestions.CountAsync(m =>
                    (m.QuestionId != null && pypQuestionIds.Contains(m.QuestionId.Value)) ||
                    (m.QuestionBankQuestionId != null && pyqQuestionIds.Contains(m.QuestionBankQuestionId.Value))),
                QuizUsages = await _db.QuizQuestions.CountAsync(q => pyqQuestionIds.Contains(q.QuestionBankQuestionId)),
                PracticeTestUsages = await _db.PracticeTestTemplateQuestions.CountAsync(p =>
                    (p.QuestionId != null && pypQuestionIds.Contains(p.QuestionId.Value)) ||
                    (p.QuestionBankQuestionId != null && pyqQuestionIds.Contains(p.QuestionBankQuestionId.Value))),
                Comments = await _db.QuestionComments.CountAsync(c =>
                    (c.QuestionId != null && pypQuestionIds.Contains(c.QuestionId.Value)) ||
                    (c.QuestionBankQuestionId != null && pyqQuestionIds.Contains(c.QuestionBankQuestionId.Value))),
                Votes = await _db.QuestionVotes.CountAsync(v =>
                    (v.QuestionId != null && pypQuestionIds.Contains(v.QuestionId.Value)) ||
                    (v.QuestionBankQuestionId != null && pyqQuestionIds.Contains(v.QuestionBankQuestionId.Value))),
                Reports = await _db.QuestionReports.CountAsync(r =>
                    (r.QuestionId != null && pypQuestionIds.Contains(r.QuestionId.Value)) ||
                    (r.QuestionBankQuestionId != null && pyqQuestionIds.Contains(r.QuestionBankQuestionId.Value))),
                Solutions = await _db.QuestionSolutions.CountAsync(s =>
                    (s.QuestionId != null && pypQuestionIds.Contains(s.QuestionId.Value)) ||
                    (s.QuestionBankQuestionId != null && pyqQuestionIds.Contains(s.QuestionBankQuestionId.Value))),
            };

            return Ok(new ExamDeleteOptionsDto
            {
                ExamId = exam.Id,
                ExamName = exam.Name,
                Papers = papers,
                PyqYears = pyqYears,
                LegacyQuestionCount = legacyQuestionCount,
                Usage = usage
            });
        }

        // POST /api/admin/exams/{id}/delete-cascade  (SuperAdmin only) -- the actual force-delete the
        // GetDeleteOptions screen leads into. Unlike the plain Delete above (which refuses unless the
        // exam is already empty), this genuinely removes real student activity (answers, bookmarks,
        // mock-test/quiz/practice-test entries, comments, votes, reports) right along with whatever
        // content it's attached to, rather than blocking -- the strict warning + typed-name
        // confirmation on the way in is the safeguard here, not a runtime refusal. Either DeleteAll,
        // or a specific selection of PaperIds and/or PyqYears (both may be given together -- e.g.
        // "these 3 old papers AND the 2019 PYQ batch", leaving everything else on the exam alone).
        // Runs inside one DB transaction so a failure partway through leaves nothing half-deleted;
        // best-effort image cleanup happens only after that transaction commits.
        [HttpPost("/api/admin/exams/{id:guid}/delete-cascade")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult<ExamDeleteResultDto>> DeleteCascade(Guid id, ExamDeleteRequestDto dto)
        {
            var exam = await _db.Exams.FirstOrDefaultAsync(e => e.Id == id);
            if (exam == null) return NotFound();

            if (!string.Equals(dto.ConfirmExamName?.Trim(), exam.Name, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "The typed exam name doesn't match -- nothing was deleted." });

            var hasPaperSelection = dto.PaperIds != null && dto.PaperIds.Count > 0;
            var hasYearSelection = dto.PyqYears != null && dto.PyqYears.Count > 0;
            if (!dto.DeleteAll && !hasPaperSelection && !hasYearSelection)
                return BadRequest(new { message = "Select at least one paper or PYQ year to delete, or choose \"delete everything\"." });

            await using var transaction = await _db.Database.BeginTransactionAsync();

            var result = new ExamDeleteResultDto();
            var imageUrlsToDelete = new List<string?>();

            // ---------- PYP side: target Papers, and every Question under them ----------
            var targetPapers = dto.DeleteAll
                ? await _db.Papers.Where(p => p.ExamId == id).ToListAsync()
                : hasPaperSelection
                    ? await _db.Papers.Where(p => p.ExamId == id && dto.PaperIds!.Contains(p.Id)).ToListAsync()
                    : new List<Paper>();

            if (targetPapers.Count > 0)
            {
                var targetPaperIds = targetPapers.Select(p => p.Id).ToList();
                var questions = await _db.Questions.Where(q => q.PaperId != null && targetPaperIds.Contains(q.PaperId.Value)).ToListAsync();
                var questionIds = questions.Select(q => q.Id).ToList();

                imageUrlsToDelete.AddRange(questions.SelectMany(q => new[]
                {
                    q.QuestionImageUrl, q.OptionAImageUrl, q.OptionBImageUrl, q.OptionCImageUrl, q.OptionDImageUrl, q.ExplanationImageUrl
                }));

                // These four are Restrict FKs on Question (see ScoramDbContext) -- must be cleared
                // before the Question rows themselves go, which happens via the Paper cascade below,
                // or SQL Server blocks the whole delete. QuestionSolution/Report/Comment/Vote are
                // Cascade already and don't need explicit handling.
                _db.MockTestQuestions.RemoveRange(_db.MockTestQuestions.Where(m => m.QuestionId != null && questionIds.Contains(m.QuestionId.Value)));
                _db.StudentAnswers.RemoveRange(_db.StudentAnswers.Where(a => a.QuestionId != null && questionIds.Contains(a.QuestionId.Value)));
                _db.PracticeTestTemplateQuestions.RemoveRange(_db.PracticeTestTemplateQuestions.Where(p => p.QuestionId != null && questionIds.Contains(p.QuestionId.Value)));
                _db.Bookmarks.RemoveRange(_db.Bookmarks.Where(b => b.QuestionId != null && questionIds.Contains(b.QuestionId.Value)));
                await _db.SaveChangesAsync();

                _db.Papers.RemoveRange(targetPapers); // cascades to Questions, and from there to their own Solutions/Reports/Comments/Votes
                await _db.SaveChangesAsync();

                result.PapersDeleted = targetPapers.Count;
                result.PypQuestionsDeleted = questionIds.Count;
            }

            // ---------- Pre-Paper-era standalone Questions (ExamId set directly) ----------
            // Only ever removed by DeleteAll -- there's no per-item identity to select against for
            // these (see ExamDeleteOptionsDto.LegacyQuestionCount's own comment).
            if (dto.DeleteAll)
            {
                var legacyQuestions = await _db.Questions.Where(q => q.ExamId == id).ToListAsync();
                if (legacyQuestions.Count > 0)
                {
                    var legacyIds = legacyQuestions.Select(q => q.Id).ToList();
                    imageUrlsToDelete.AddRange(legacyQuestions.SelectMany(q => new[]
                    {
                        q.QuestionImageUrl, q.OptionAImageUrl, q.OptionBImageUrl, q.OptionCImageUrl, q.OptionDImageUrl, q.ExplanationImageUrl
                    }));

                    _db.MockTestQuestions.RemoveRange(_db.MockTestQuestions.Where(m => m.QuestionId != null && legacyIds.Contains(m.QuestionId.Value)));
                    _db.StudentAnswers.RemoveRange(_db.StudentAnswers.Where(a => a.QuestionId != null && legacyIds.Contains(a.QuestionId.Value)));
                    _db.PracticeTestTemplateQuestions.RemoveRange(_db.PracticeTestTemplateQuestions.Where(p => p.QuestionId != null && legacyIds.Contains(p.QuestionId.Value)));
                    _db.Bookmarks.RemoveRange(_db.Bookmarks.Where(b => b.QuestionId != null && legacyIds.Contains(b.QuestionId.Value)));
                    await _db.SaveChangesAsync();

                    _db.Questions.RemoveRange(legacyQuestions);
                    await _db.SaveChangesAsync();

                    result.PypQuestionsDeleted += legacyIds.Count;
                }
            }

            // ---------- PYQ side: target QuestionBankExamMapping rows for this exam ----------
            var mappingQuery = _db.QuestionBankExamMappings.Where(m => m.ExamId == id);
            var targetMappings = dto.DeleteAll
                ? await mappingQuery.ToListAsync()
                : hasYearSelection
                    ? await mappingQuery.Where(m => dto.PyqYears!.Contains(m.Year)).ToListAsync()
                    : new List<QuestionBankExamMapping>();

            if (targetMappings.Count > 0)
            {
                var affectedQuestionIds = targetMappings.Select(m => m.QuestionBankQuestionId).Distinct().ToList();
                _db.QuestionBankExamMappings.RemoveRange(targetMappings);
                await _db.SaveChangesAsync();

                // A question whose LAST mapping was just removed is now tagged to no exam at all --
                // fully delete it (and its own Restrict dependents) rather than leave untagged,
                // unreachable content sitting in the Question Bank. A question that still has a
                // mapping to another exam (shared content -- see the design note this followed) is
                // left completely alone; only this exam's own mapping row was ever touched for it.
                var stillMappedIds = await _db.QuestionBankExamMappings
                    .Where(m => affectedQuestionIds.Contains(m.QuestionBankQuestionId))
                    .Select(m => m.QuestionBankQuestionId)
                    .Distinct()
                    .ToListAsync();
                var orphanedIds = affectedQuestionIds.Except(stillMappedIds).ToList();

                result.PyqMappingsRemoved = targetMappings.Count - orphanedIds.Count;

                if (orphanedIds.Count > 0)
                {
                    var orphanedQuestions = await _db.QuestionBankQuestions.Where(q => orphanedIds.Contains(q.Id)).ToListAsync();
                    imageUrlsToDelete.AddRange(orphanedQuestions.SelectMany(q => new[]
                    {
                        q.QuestionImageUrl, q.OptionAImageUrl, q.OptionBImageUrl, q.OptionCImageUrl, q.OptionDImageUrl, q.ExplanationImageUrl
                    }));

                    // Same idea as the PYP side above -- these six are Restrict FKs on
                    // QuestionBankQuestion; Solutions/Reports/Comments/Votes are Cascade and don't
                    // need explicit handling, and ChatMessage/DirectMessage shares SetNull themselves.
                    _db.MockTestQuestions.RemoveRange(_db.MockTestQuestions.Where(m => m.QuestionBankQuestionId != null && orphanedIds.Contains(m.QuestionBankQuestionId.Value)));
                    _db.QuizQuestions.RemoveRange(_db.QuizQuestions.Where(q => orphanedIds.Contains(q.QuestionBankQuestionId)));
                    _db.StudentAnswers.RemoveRange(_db.StudentAnswers.Where(a => a.QuestionBankQuestionId != null && orphanedIds.Contains(a.QuestionBankQuestionId.Value)));
                    _db.PracticeTestTemplateQuestions.RemoveRange(_db.PracticeTestTemplateQuestions.Where(p => p.QuestionBankQuestionId != null && orphanedIds.Contains(p.QuestionBankQuestionId.Value)));
                    _db.PaperQuestionBankLinks.RemoveRange(_db.PaperQuestionBankLinks.Where(l => orphanedIds.Contains(l.QuestionBankQuestionId)));
                    _db.Bookmarks.RemoveRange(_db.Bookmarks.Where(b => b.QuestionBankQuestionId != null && orphanedIds.Contains(b.QuestionBankQuestionId.Value)));
                    await _db.SaveChangesAsync();

                    _db.QuestionBankQuestions.RemoveRange(orphanedQuestions); // cascades own Solutions/Reports/Comments/Votes
                    await _db.SaveChangesAsync();

                    result.PyqQuestionsDeleted = orphanedIds.Count;
                }
            }

            // ---------- Exam row itself ----------
            // Only removed if genuinely nothing is left on it -- same emptiness check Delete/
            // CleanupIfEmpty already use, so a partial (selective) delete that leaves other Papers/
            // PYQ years/activity behind correctly keeps the Exam around for those.
            if (!await ExamHasContentAsync(_db, id, exam.Name))
            {
                var room = await _db.ChatRooms.FirstOrDefaultAsync(r => r.ExamId == id);
                if (room != null) _db.ChatRooms.Remove(room);
                _db.Exams.Remove(exam);
                await _db.SaveChangesAsync();
                result.ExamDeleted = true;
            }

            await transaction.CommitAsync();

            // Best-effort, after the transaction that actually matters has already committed -- same
            // "cleanup nicety, never fails the request" contract as everywhere else this is done.
            foreach (var url in imageUrlsToDelete) await _fileStorage.DeleteImageAsync(url);

            await _audit.LogAsync(User.GetAdminId(), "Exam.DeleteCascade", "Exam", id,
                $"\"{exam.Name}\": {result.PapersDeleted} paper(s), {result.PypQuestionsDeleted} PYP question(s), " +
                $"{result.PyqQuestionsDeleted} PYQ question(s) fully removed, {result.PyqMappingsRemoved} PYQ mapping(s) removed" +
                (result.ExamDeleted ? " -- exam itself deleted" : " -- exam kept (still has other content)"));

            return Ok(result);
        }

        // DELETE /api/admin/exams/{id}/empty-cleanup  (DeletePaper permission -- Admin or SuperAdmin,
        // NOT restricted to SuperAdmin like Delete above) -- a narrower sibling reached only from the
        // bulk-import Undo flow (see BulkImportController.Rollback's ExamCleanupCandidateId), after
        // the admin confirms a "this exam has nothing else on it -- delete it too?" prompt. Runs the
        // EXACT same emptiness check as Delete, so it can never remove an exam that has real content --
        // the only difference from Delete is who's allowed to call it. Delete stays SuperAdmin-only
        // for "delete any exam, however old, however it became empty"; this one is scoped to the
        // narrow, low-risk case a bulk-upload undo just created, so a regular Admin trusted with
        // DeletePaper doesn't need to escalate to a Super Admin just to clear out their own mistake.
        [HttpDelete("/api/admin/exams/{id:guid}/empty-cleanup")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> CleanupIfEmpty(Guid id)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.DeletePaper))
                return Forbid();

            var exam = await _db.Exams.FirstOrDefaultAsync(e => e.Id == id);
            if (exam == null) return NotFound();

            if (await ExamHasContentAsync(_db, id, exam.Name))
                return Conflict(new { message = "This exam is no longer empty -- it can't be cleaned up automatically anymore." });

            var room = await _db.ChatRooms.FirstOrDefaultAsync(r => r.ExamId == id);
            if (room != null) _db.ChatRooms.Remove(room);
            _db.Exams.Remove(exam);
            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), "Exam.CleanupEmpty", "Exam", id, $"\"{exam.Name}\" removed as part of a bulk-import undo");

            return NoContent();
        }

        // Shared by QuestionBankAdminController's book-import commit and
        // BulkPaperImportController's paper-shell commit -- a cached exam resolver used inside a
        // bulk-commit loop, to avoid one SELECT (and possibly one INSERT) round trip per row when a
        // batch contributes many rows under the same handful of exams. `cache` should be pre-seeded
        // by the caller, once, from db.Exams.ToDictionaryAsync(e => e.Name, StringComparer.OrdinalIgnoreCase)
        // before the loop starts, so an exam created by an earlier row in this same batch is found
        // here instead of creating a second duplicate exam with the same name.
        internal static async Task<Exam> GetOrCreateExamCachedAsync(ScoramDbContext db, string examName, Guid adminId, Dictionary<string, Exam> cache)
        {
            var name = examName.Trim();
            if (cache.TryGetValue(name, out var cached)) return cached;

            var exam = new Exam { Name = name, CreatedByAdminId = adminId };
            db.Exams.Add(exam);
            // Every newly-created exam needs a chat room -- see Create() above, which does this for
            // exams created via Admin > Exams; this is the equivalent for one created mid-bulk-import.
            db.ChatRooms.Add(new ChatRoom
            {
                ExamId = exam.Id,
                Name = exam.Name,
                Description = $"Discussion room for {exam.Name} aspirants",
                IsFeatured = false,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            cache[name] = exam;
            return exam;
        }

        // TOTAL QUESTIONS FIX -- an exam's "Total Questions" must be PYP (legacy Question rows +
        // Paper-attached Questions) PLUS PYQ (Question Bank questions tagged to this exam via
        // QuestionBankExamMapping -- the separate, independent-of-any-Paper question bank, section
        // 21 of the spec). Previously this only ever counted the PYP half, so an exam with e.g. 100
        // PYP questions and 73 PYQ questions showed "100" everywhere instead of "173".
        //
        // IMPORTANT -- do NOT just add the two counts raw. IQuestionBankMirrorService auto-copies
        // every PYP question into the Question Bank the moment it's created (see
        // QuestionsController.Create/BulkImportController.Commit, tracked via
        // Question.MirroredToQuestionBankQuestionId). That mirror exists purely so the PYP content
        // is searchable/reusable elsewhere -- it's the SAME question, not a second one. Counting it
        // on both sides would inflate every exam's total by its own mirrored-question count (e.g.
        // 100 PYP + 73 genuinely-separate PYQ would wrongly show 100 + (100 mirrors + 73) = 273
        // instead of 173). So the PYQ half here only counts bank questions NOT pointed at by any
        // Question.MirroredToQuestionBankQuestionId -- i.e. questions actually added straight to the
        // Question Bank, not a paper's own question surfacing there for search.
        //
        // Used by the single-exam endpoints below (Update/SetBlocked); List/AdminList use the bulk
        // GetQuestionBankCountsByExamAsync version instead since those return many exams at once.
        // .Any()-based (not a mapping count) so a question tagged to the same exam across several
        // years (see QuestionBankExamMapping.Year) still only counts once.
        internal static async Task<int> GetCombinedQuestionCountAsync(ScoramDbContext db, Guid examId)
        {
            var pypCount = await db.Questions.CountAsync(q => q.ExamId == examId)
                + await db.Papers.Where(p => p.ExamId == examId).SelectMany(p => p.Questions).CountAsync();
            var pyqCount = await db.QuestionBankQuestions
                .CountAsync(x => x.IsActive
                    && x.ExamMappings.Any(m => m.ExamId == examId)
                    && !db.Questions.Any(q => q.MirroredToQuestionBankQuestionId == x.Id));
            return pypCount + pyqCount;
        }

        // Bulk sibling of GetCombinedQuestionCountAsync's PYQ half, for List/AdminList which return
        // every exam in one round trip -- one aggregate query instead of one subquery per exam.
        // Same two safeguards as the single-exam version above: excludes bank questions that are
        // just an auto-mirror of an already-counted PYP question, and dedupes (QuestionId, ExamId)
        // pairs so a question tagged to one exam across multiple years only counts once for it.
        private static async Task<Dictionary<Guid, int>> GetQuestionBankCountsByExamAsync(ScoramDbContext db)
        {
            var mirroredBankQuestionIds = await db.Questions
                .Where(q => q.MirroredToQuestionBankQuestionId != null)
                .Select(q => q.MirroredToQuestionBankQuestionId!.Value)
                .Distinct()
                .ToListAsync();
            var mirroredSet = mirroredBankQuestionIds.ToHashSet();

            var pairs = await db.QuestionBankQuestions
                .Where(x => x.IsActive)
                .SelectMany(x => x.ExamMappings.Select(m => new { QuestionId = x.Id, m.ExamId }))
                .Distinct()
                .ToListAsync();

            return pairs
                .Where(p => !mirroredSet.Contains(p.QuestionId))
                .GroupBy(p => p.ExamId)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        // Shared by Delete, CleanupIfEmpty above, and PapersController.Create (which stamps
        // Paper.ExamCreatedForThisPaper using this exact same check, at the moment a new Paper is
        // created under an Exam) -- one single definition of "genuinely empty" used everywhere, so
        // the call sites can never quietly drift out of sync with each other.
        internal static async Task<bool> ExamHasContentAsync(ScoramDbContext db, Guid examId, string examName)
        {
            var hasContent = await db.Questions.AnyAsync(q => q.ExamId == examId)
                || await db.Papers.AnyAsync(p => p.ExamId == examId)
                || await db.QuestionBankExamMappings.AnyAsync(m => m.ExamId == examId)
                || await db.PracticeTestTemplates.AnyAsync(t => t.ExamId == examId)
                || await db.MockTests.AnyAsync(m => m.ExamName == examName);

            if (!hasContent)
            {
                var room = await db.ChatRooms.FirstOrDefaultAsync(r => r.ExamId == examId);
                if (room != null)
                {
                    hasContent = await db.ChatMessages.AnyAsync(m => m.ChatRoomId == room.Id)
                        || await db.ChatRoomMemberships.AnyAsync(m => m.ChatRoomId == room.Id);
                }
            }

            return hasContent;
        }

        private string? ValidateLogo(IFormFile logo)
        {
            if (logo.Length == 0) return "The uploaded logo file is empty.";
            if (logo.Length > MaxLogoSizeBytes) return "Logo must be 2 MB or smaller.";

            var ext = Path.GetExtension(logo.FileName).ToLowerInvariant();
            if (!AllowedLogoExtensions.Contains(ext))
                return $"Logo must be one of: {string.Join(", ", AllowedLogoExtensions)}.";

            return null;
        }
    }
}
