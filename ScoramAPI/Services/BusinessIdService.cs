using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    // Same idea as SubjectOpException: an "expected" failure carrying the HTTP status and a stable
    // machine-readable code, so the controller can return { message, code } instead of a raw 500.
    public class BusinessIdException : Exception
    {
        public int Status { get; }
        public string Code { get; }

        public BusinessIdException(int status, string code, string message) : base(message)
        {
            Status = status;
            Code = code;
        }
    }

    public interface IBusinessIdService
    {
        /// <summary>SuperAdmin-only. Replaces one record's Business ID after validating format,
        /// uniqueness and history, inside one transaction together with its audit-log row. The
        /// record's GUID and every foreign key pointing at it are untouched.</summary>
        Task<BusinessIdChangeResultDto> ChangeAsync(Guid actorAdminId, BusinessIdChangeRequestDto dto, CancellationToken ct);

        /// <summary>Gives every record that has no Business ID yet its first one, deterministically
        /// (oldest first, GUID as tie-break). Idempotent. With dryRun=true the whole thing runs inside
        /// a transaction that is rolled back, so the returned plan is exactly what a real run would do.</summary>
        Task<BusinessIdBackfillResultDto> BackfillAsync(bool dryRun, CancellationToken ct);
    }

    public class BusinessIdService : IBusinessIdService
    {
        private const string BackfillLockResource = "ScoramBusinessIdBackfill";

        private readonly ScoramDbContext _db;
        private readonly ILogger<BusinessIdService> _logger;

        public BusinessIdService(ScoramDbContext db, ILogger<BusinessIdService> logger)
        {
            _db = db;
            _logger = logger;
        }

        // ------------------------------------------------------------------------------
        // SuperAdmin change
        // ------------------------------------------------------------------------------

        public async Task<BusinessIdChangeResultDto> ChangeAsync(Guid actorAdminId, BusinessIdChangeRequestDto dto, CancellationToken ct)
        {
            if (!BusinessIdFormats.TryParseEntityType(dto.EntityType, out var type))
                throw new BusinessIdException(400, "INVALID_ENTITY_TYPE", "Unknown entity type. Use exam, subject, test, mocktest or admin.");

            var newId = BusinessIdFormats.Normalize(dto.NewBusinessId);
            if (!BusinessIdFormats.IsValid(type, newId))
                throw new BusinessIdException(400, "INVALID_FORMAT",
                    $"That isn't a valid {Describe(type)} Business ID. Expected a format like {BusinessIdFormats.ExampleFor(type)}.");

            if (!dto.Confirm)
                throw new BusinessIdException(400, "CONFIRMATION_REQUIRED", "Please confirm this change before continuing.");

            // Authorization is enforced on the controller too ([Authorize(Roles = "SuperAdmin")]); this
            // re-checks the LIVE account so a token issued before a demotion/deactivation can't be used.
            var actor = await _db.Admins.AsNoTracking()
                .Where(a => a.Id == actorAdminId)
                .Select(a => new { a.Role, a.IsActive, a.BusinessId })
                .FirstOrDefaultAsync(ct);
            if (actor == null || !actor.IsActive || actor.Role != AdminRole.SuperAdmin)
                throw new BusinessIdException(403, "SUPERADMIN_REQUIRED", "Only a SuperAdmin can change a Business ID.");

            if (!await BusinessIdGenerator.IsBackfillCompleteAsync(_db, ct))
                throw new BusinessIdException(409, "BACKFILL_PENDING",
                    "Existing records haven't been given Business IDs yet. Run the Business ID backfill first.");

            var (entity, name) = await LoadAsync(type, dto.EntityId, ct);
            var oldId = entity.BusinessId;

            if (string.Equals(oldId, newId, StringComparison.OrdinalIgnoreCase))
                throw new BusinessIdException(400, "NO_CHANGE", $"This record already has the Business ID {newId}.");

            // An ID that has EVER been issued can't go to a different record -- even if its original
            // record was deleted, merged away or renumbered. The one exception: moving a record back to
            // an ID that was originally its own (undoing a mistaken change).
            var issued = await _db.BusinessIdRegistry.AsNoTracking().FirstOrDefaultAsync(r => r.BusinessId == newId, ct);
            if (issued != null && issued.EntityId != entity.Id)
                throw new BusinessIdException(409, "DUPLICATE_BUSINESS_ID",
                    $"{newId} is already in use, or was used before by another record, and Business IDs are never reused.");
            if (await HeldByAnotherAsync(type, newId, entity.Id, ct))
                throw new BusinessIdException(409, "DUPLICATE_BUSINESS_ID", $"{newId} is already assigned to another record.");

            var actorLabel = actor.BusinessId ?? actorAdminId.ToString();
            var detail = $"Business ID changed {oldId ?? "(none)"} -> {newId} for {type} \"{Trim(name, 100)}\" (GUID {entity.Id}). Changed by {actorLabel}.";

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                _db.AllowBusinessIdChange = true;
                entity.BusinessId = newId;

                if (issued == null)
                {
                    _db.BusinessIdRegistry.Add(new BusinessIdRegistryEntry
                    {
                        BusinessId = newId,
                        EntityType = type.ToString(),
                        EntityId = entity.Id,
                        IssuedAt = DateTime.UtcNow
                    });
                }

                // Written in the same transaction as the change itself: either both exist or neither.
                _db.AuditLogs.Add(new AuditLog
                {
                    AdminId = actorAdminId,
                    Action = "BusinessId.Change",
                    TargetType = type.ToString(),
                    TargetId = entity.Id,
                    Detail = Trim(detail, 1000)
                });

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                await tx.RollbackAsync(CancellationToken.None);
                // Lost a race with someone else taking the same ID between the checks and the write.
                throw new BusinessIdException(409, "DUPLICATE_BUSINESS_ID", $"{newId} was just taken by another record. Nothing was changed.");
            }
            catch
            {
                await tx.RollbackAsync(CancellationToken.None);
                throw;
            }
            finally
            {
                _db.AllowBusinessIdChange = false;
            }

            _logger.LogWarning("Business ID changed: {Type} {EntityId} {Old} -> {New} by admin {Actor}", type, entity.Id, oldId, newId, actorAdminId);

            return new BusinessIdChangeResultDto
            {
                Message = $"Business ID changed to {newId}.",
                EntityType = type.ToString(),
                EntityId = entity.Id,
                Name = name,
                OldBusinessId = oldId,
                NewBusinessId = newId
            };
        }

        private async Task<(IHasBusinessId Entity, string Name)> LoadAsync(BusinessIdEntityType type, Guid id, CancellationToken ct)
        {
            switch (type)
            {
                case BusinessIdEntityType.Exam:
                {
                    var e = await _db.Exams.FirstOrDefaultAsync(x => x.Id == id, ct);
                    if (e != null) return (e, e.Name);
                    break;
                }
                case BusinessIdEntityType.Subject:
                {
                    var e = await _db.QuestionBankSubjects.FirstOrDefaultAsync(x => x.Id == id, ct);
                    if (e != null) return (e, e.Name);
                    break;
                }
                case BusinessIdEntityType.Test:
                {
                    var e = await _db.PracticeTestTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);
                    if (e != null) return (e, e.Title);
                    break;
                }
                case BusinessIdEntityType.MockTest:
                {
                    var e = await _db.MockTests.FirstOrDefaultAsync(x => x.Id == id, ct);
                    if (e != null) return (e, e.Title);
                    break;
                }
                case BusinessIdEntityType.Admin:
                {
                    var e = await _db.Admins.FirstOrDefaultAsync(x => x.Id == id, ct);
                    if (e != null) return (e, e.FullName);
                    break;
                }
                case BusinessIdEntityType.SharedStimulus:
                {
                    var e = await _db.SharedStimuli.FirstOrDefaultAsync(x => x.Id == id, ct);
                    if (e != null) return (e, e.Title);
                    break;
                }
            }
            throw new BusinessIdException(404, "NOT_FOUND", $"That {Describe(type)} could not be found.");
        }

        private Task<bool> HeldByAnotherAsync(BusinessIdEntityType type, string businessId, Guid exceptId, CancellationToken ct) => type switch
        {
            BusinessIdEntityType.Exam => _db.Exams.AnyAsync(x => x.BusinessId == businessId && x.Id != exceptId, ct),
            BusinessIdEntityType.Subject => _db.QuestionBankSubjects.AnyAsync(x => x.BusinessId == businessId && x.Id != exceptId, ct),
            BusinessIdEntityType.Test => _db.PracticeTestTemplates.AnyAsync(x => x.BusinessId == businessId && x.Id != exceptId, ct),
            BusinessIdEntityType.MockTest => _db.MockTests.AnyAsync(x => x.BusinessId == businessId && x.Id != exceptId, ct),
            BusinessIdEntityType.Admin => _db.Admins.AnyAsync(x => x.BusinessId == businessId && x.Id != exceptId, ct),
            BusinessIdEntityType.SharedStimulus => _db.SharedStimuli.AnyAsync(x => x.BusinessId == businessId && x.Id != exceptId, ct),
            _ => Task.FromResult(false)
        };

        // ------------------------------------------------------------------------------
        // One-time backfill of existing records
        // ------------------------------------------------------------------------------

        public async Task<BusinessIdBackfillResultDto> BackfillAsync(bool dryRun, CancellationToken ct)
        {
            if (_db.Database.CurrentTransaction != null)
                throw new InvalidOperationException("Backfill must own its transaction.");

            var result = new BusinessIdBackfillResultDto { DryRun = dryRun };

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                // Two app instances starting at the same moment must not both number the same rows:
                // an exclusive transaction-scoped application lock makes the second one wait, then find
                // nothing left to do.
                await AcquireBackfillLockAsync(ct);

                // Oldest first, GUID as the tie-break: the same data always produces the same IDs, and
                // "the first exam ever created" really is EXMSSC001. No randomness anywhere.
                var admins = await _db.Admins.Where(a => a.BusinessId == null)
                    .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).ToListAsync(ct);
                var subjects = await _db.QuestionBankSubjects.Where(s => s.BusinessId == null)
                    .OrderBy(s => s.CreatedAt).ThenBy(s => s.Id).ToListAsync(ct);
                var exams = await _db.Exams.Include(e => e.Organization).Where(e => e.BusinessId == null)
                    .OrderBy(e => e.CreatedAt).ThenBy(e => e.Id).ToListAsync(ct);
                var tests = await _db.PracticeTestTemplates.Where(t => t.BusinessId == null)
                    .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).ToListAsync(ct);
                var mocks = await _db.MockTests.Where(m => m.BusinessId == null)
                    .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).ToListAsync(ct);

                await BusinessIdGenerator.AssignAsync(_db, admins, ct);
                await BusinessIdGenerator.AssignAsync(_db, subjects, ct);
                await BusinessIdGenerator.AssignAsync(_db, exams, ct);
                await BusinessIdGenerator.AssignAsync(_db, tests, ct);
                await BusinessIdGenerator.AssignAsync(_db, mocks, ct);

                await _db.SaveChangesAsync(ct);

                result.Admins = admins.Count;
                result.Subjects = subjects.Count;
                result.Exams = exams.Count;
                result.Tests = tests.Count;
                result.MockTests = mocks.Count;

                result.Items.AddRange(admins.Select(a => Item(BusinessIdEntityType.Admin, a, a.FullName)));
                result.Items.AddRange(subjects.Select(s => Item(BusinessIdEntityType.Subject, s, s.Name)));
                result.Items.AddRange(exams.Select(e => Item(BusinessIdEntityType.Exam, e, e.Name)));
                result.Items.AddRange(tests.Select(t => Item(BusinessIdEntityType.Test, t, t.Title)));
                result.Items.AddRange(mocks.Select(m => Item(BusinessIdEntityType.MockTest, m, m.Title)));

                if (dryRun)
                {
                    await tx.RollbackAsync(CancellationToken.None);
                    _db.ChangeTracker.Clear();
                    result.Completed = await BusinessIdGenerator.IsBackfillCompleteAsync(_db, ct);
                    return result;
                }

                await BusinessIdGenerator.MarkBackfillCompleteAsync(_db, ct);
                await tx.CommitAsync(ct);
                BusinessIdGenerator.RememberBackfillComplete();
                result.Completed = true;
            }
            catch
            {
                await tx.RollbackAsync(CancellationToken.None);
                _db.ChangeTracker.Clear();
                throw;
            }

            _logger.LogInformation(
                "Business ID backfill complete: {Exams} exams, {Subjects} subjects, {Tests} tests, {MockTests} mock tests, {Admins} admins.",
                result.Exams, result.Subjects, result.Tests, result.MockTests, result.Admins);
            return result;
        }

        private async Task AcquireBackfillLockAsync(CancellationToken ct)
        {
            var conn = _db.Database.GetDbConnection();
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = _db.Database.CurrentTransaction!.GetDbTransaction();
            cmd.CommandText =
                "DECLARE @r int; " +
                $"EXEC @r = sp_getapplock @Resource = N'{BackfillLockResource}', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 60000; " +
                "SELECT @r;";
            var rc = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            if (rc < 0)
                throw new InvalidOperationException($"Could not acquire the Business ID backfill lock (sp_getapplock returned {rc}).");
        }

        // Called once at startup (Program.cs). Idempotent: on every later start there is nothing left
        // to number and it just re-asserts the completion marker. Set BusinessIds:AutoBackfillOnStartup
        // to false to run it manually (dry run first) via POST /api/admin/business-ids/backfill instead.
        public static async Task RunStartupBackfillAsync(IServiceProvider services, IConfiguration config, ILogger logger)
        {
            if (!config.GetValue("BusinessIds:AutoBackfillOnStartup", true))
            {
                logger.LogInformation("Business ID backfill on startup is disabled (BusinessIds:AutoBackfillOnStartup=false).");
                return;
            }

            using var scope = services.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<IBusinessIdService>();
            await svc.BackfillAsync(dryRun: false, CancellationToken.None);
        }

        // ------------------------------------------------------------------------------

        private static BusinessIdBackfillItemDto Item(BusinessIdEntityType type, IHasBusinessId e, string name) => new()
        {
            EntityType = type.ToString(),
            EntityId = e.Id,
            Name = name,
            BusinessId = e.BusinessId ?? string.Empty
        };

        private static string Describe(BusinessIdEntityType type) => type switch
        {
            BusinessIdEntityType.MockTest => "mock test",
            BusinessIdEntityType.Test => "test",
            BusinessIdEntityType.SharedStimulus => "shared stimulus",
            _ => type.ToString().ToLowerInvariant()
        };

        private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 3)] + "...";

        private static bool IsUniqueViolation(DbUpdateException ex) =>
            ex.InnerException is SqlException sql && (sql.Number == 2601 || sql.Number == 2627);
    }
}
