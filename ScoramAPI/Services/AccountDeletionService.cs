using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;

namespace ScoramAPI.Services
{
    public interface IAccountDeletionService
    {
        /// <summary>
        /// Permanently erases a student's personal data and anonymizes their account. Callers MUST
        /// have already verified the person's identity (see AuthController.DeleteAccount) -- this
        /// method does not re-authenticate anyone. Returns false only if the account doesn't exist
        /// or was already deleted.
        /// </summary>
        Task<bool> DeleteAccountAsync(Guid userId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// SELF-SERVICE ACCOUNT DELETION (Google Play "account deletion" requirement).
    ///
    /// Design: the Users row is ANONYMIZED IN PLACE ("tombstoned") rather than hard-deleted. About
    /// twenty foreign keys point at Users with Restrict/NoAction behavior (chat/DM senders, poll
    /// votes, moderation reports, referrals, discussion authors, ...), and QuestionComment.UserId
    /// == null already means "written by an admin" -- so nulling authorship would misrepresent
    /// content. A tombstone keeps every FK valid and lets retained community content read as
    /// "Deleted User", while the row itself no longer holds anything that identifies a person.
    ///
    /// ERASED (hard-deleted): test/quiz/mock/PYP attempts + answers, XP/streaks/badges, bookmarks,
    /// My Exams preferences, feedback, device (FCM) tokens, web-push subscriptions, notifications,
    /// refresh tokens (these carried the client IP), votes, group memberships, @mentions of the
    /// user, Study Partner requests/partnerships/blocks/challenges/privacy settings, quiz
    /// challenges, Document rows uploaded by the user, and the stored blobs behind them.
    ///
    /// CLEARED IN PLACE: the user's Direct Message content and attachments (messages stay as
    /// "Message deleted" for the other person -- the existing soft-delete convention), group-chat
    /// attachments, and question-report proof images.
    ///
    /// RETAINED, now attributed to "Deleted User": text posted in group chat, discussion
    /// comments and solutions, poll votes, moderation reports (reason text only), and referral
    /// rows. This is a deliberate community-integrity choice and is disclosed in the Privacy
    /// Policy -- change here AND there if the policy ever changes.
    ///
    /// Everything database-side runs in ONE transaction, so a failure leaves the account fully
    /// intact. Blob deletion and cache invalidation happen after commit and are best-effort
    /// (an orphaned blob is harmless; failing a completed deletion over it would not be).
    /// </summary>
    public class AccountDeletionService : IAccountDeletionService
    {
        // Registration only allows [a-z0-9._] in usernames (see RegisterDto), so a hyphen here can
        // never collide with a real account.
        public const string TombstoneUsernamePrefix = "deleted-";

        private readonly ScoramDbContext _db;
        private readonly IFileStorageService _fileStorage;
        private readonly IAzureBlobService _blobService;
        private readonly ISecurityStampService _securityStamps;
        private readonly ILogger<AccountDeletionService> _logger;

        public AccountDeletionService(
            ScoramDbContext db,
            IFileStorageService fileStorage,
            IAzureBlobService blobService,
            ISecurityStampService securityStamps,
            ILogger<AccountDeletionService> logger)
        {
            _db = db;
            _fileStorage = fileStorage;
            _blobService = blobService;
            _securityStamps = securityStamps;
            _logger = logger;
        }

        public async Task<bool> DeleteAccountAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user == null || user.Username.StartsWith(TombstoneUsernamePrefix, StringComparison.Ordinal))
                return false;

            // ---- Collect stored files BEFORE the database rows that point at them are changed ----
            var uploadUrls = new List<string>();
            if (!string.IsNullOrWhiteSpace(user.PhotoUrl)) uploadUrls.Add(user.PhotoUrl!);

            uploadUrls.AddRange(await _db.DirectMessages
                .Where(m => m.SenderId == userId && m.AttachmentUrl != null)
                .Select(m => m.AttachmentUrl!)
                .ToListAsync(cancellationToken));

            uploadUrls.AddRange(await _db.ChatMessages
                .Where(m => m.UserId == userId && m.AttachmentUrl != null)
                .Select(m => m.AttachmentUrl!)
                .ToListAsync(cancellationToken));

            uploadUrls.AddRange(await _db.QuestionReports
                .Where(r => r.ReportedByUserId == userId && r.ProofUrl != null)
                .Select(r => r.ProofUrl!)
                .ToListAsync(cancellationToken));

            var documentBlobNames = await _db.Documents
                .Where(d => d.UploadedByUserId == userId)
                .Select(d => d.BlobName)
                .ToListAsync(cancellationToken);

            await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

            // ---- Attempts. Quiz challenges point at attempts (Restrict), so they go first. ----
            await _db.QuizChallenges
                .Where(c => c.ChallengerUserId == userId || c.ChallengedUserId == userId)
                .ExecuteDeleteAsync(cancellationToken);
            // StudentAnswers are removed by the database's ON DELETE CASCADE.
            await _db.StudentTestResults.Where(r => r.UserId == userId).ExecuteDeleteAsync(cancellationToken);

            // ---- Progress / gamification ----
            await _db.XpTransactions.Where(x => x.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            await _db.UserXPs.Where(x => x.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            await _db.UserStreaks.Where(s => s.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            await _db.UserBadges.Where(b => b.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            await _db.UserQuestionSolves.Where(s => s.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            await _db.StudentSyllabusProgress.Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            await _db.TypingTestResults.Where(t => t.UserId == userId).ExecuteDeleteAsync(cancellationToken);

            // ---- Preferences, saved items, feedback ----
            await _db.Bookmarks.Where(b => b.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            await _db.UserExamPreferences.Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            await _db.UserFeedbacks.Where(f => f.UserId == userId).ExecuteDeleteAsync(cancellationToken);

            // ---- Notifications and devices (FCM tokens, web-push subscriptions) ----
            await _db.DeviceTokens.Where(d => d.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            await _db.PushSubscriptions.Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            await _db.Notifications.Where(n => n.UserId == userId).ExecuteDeleteAsync(cancellationToken);

            // ---- Sessions (refresh tokens stored the client IP) ----
            await _db.RefreshTokens.Where(t => t.PrincipalId == userId && !t.IsAdmin).ExecuteDeleteAsync(cancellationToken);

            // ---- Votes, group memberships, mentions ----
            await _db.QuestionVotes.Where(v => v.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            await _db.CommentVotes.Where(v => v.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            await _db.ChatRoomMemberships.Where(m => m.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            await _db.ChatMessageMentions.Where(m => m.MentionedUserId == userId).ExecuteDeleteAsync(cancellationToken);

            // ---- Study Partner ----
            await _db.StudyPartnerChallenges
                .Where(c => c.CreatorUserId == userId || c.PartnerUserId == userId)
                .ExecuteDeleteAsync(cancellationToken);
            await _db.StudyPartnerRequests
                .Where(r => r.SenderUserId == userId || r.ReceiverUserId == userId)
                .ExecuteDeleteAsync(cancellationToken);
            await _db.StudyPartnerships
                .Where(p => p.UserAId == userId || p.UserBId == userId)
                .ExecuteDeleteAsync(cancellationToken);
            await _db.UserBlocks
                .Where(b => b.BlockerUserId == userId || b.BlockedUserId == userId)
                .ExecuteDeleteAsync(cancellationToken);
            await _db.PrivacySettings.Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);

            // ---- Uploaded document rows (blobs are removed after commit) ----
            await _db.Documents.Where(d => d.UploadedByUserId == userId).ExecuteDeleteAsync(cancellationToken);

            // ---- Direct Messages: erase what this person wrote. The conversation rows stay so the
            // other participant keeps their own history (shown as "Message deleted"). ----
            await _db.DirectMessages
                .Where(m => m.SenderId == userId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.IsDeleted, true)
                    .SetProperty(m => m.MessageText, (string?)null)
                    .SetProperty(m => m.AttachmentUrl, (string?)null)
                    .SetProperty(m => m.AttachmentDurationSeconds, (int?)null)
                    .SetProperty(m => m.SharedQuestionExamName, (string?)null)
                    .SetProperty(m => m.SharedContentTitle, (string?)null)
                    .SetProperty(m => m.SharedContentSubtitle, (string?)null),
                    cancellationToken);

            // ---- Group chat: files the user shared are removed; their text stays, anonymized. ----
            await _db.ChatMessages
                .Where(m => m.UserId == userId && m.AttachmentUrl != null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.IsDeleted, true)
                    .SetProperty(m => m.AttachmentUrl, (string?)null),
                    cancellationToken);

            // ---- Question reports are kept for content quality; only the proof upload goes. ----
            await _db.QuestionReports
                .Where(r => r.ReportedByUserId == userId && r.ProofUrl != null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ProofUrl, (string?)null), cancellationToken);

            // ---- Anonymize the account row itself ----
            var tombstoneKey = userId.ToString("N");
            user.Username = TombstoneUsernamePrefix + tombstoneKey[..12];
            user.FullName = "Deleted User";
            user.Email = $"deleted-{tombstoneKey}@deleted.scoram.invalid"; // reserved .invalid TLD
            user.PhoneNumber = "del-" + tombstoneKey[..16];
            user.PhoneVerified = false;
            // A valid BCrypt hash of a random secret nobody knows -- keeps BCrypt.Verify from
            // throwing if someone types this tombstone's username, and can never match.
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
            user.PhotoUrl = null;
            user.ReferralCode = null;
            user.BonusMockAttempts = 0;
            user.LastActiveAt = null;
            user.DmLastSeenAt = null;
            user.NotifyOnGroupMessages = false;
            user.NotifyOnDirectMessages = false;
            user.IsActive = false;
            user.SecurityStamp = Guid.NewGuid(); // every outstanding access token stops working

            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            // ---- After commit: best-effort cleanup that must never undo a completed deletion ----
            try { await _securityStamps.InvalidateAsync(userId, isAdmin: false); }
            catch (Exception ex) { _logger.LogWarning(ex, "Security-stamp cache invalidation failed after account deletion for {UserId}", userId); }

            foreach (var url in uploadUrls.Distinct())
                await _fileStorage.DeleteImageAsync(url); // swallows its own errors

            foreach (var blobName in documentBlobNames.Distinct())
            {
                try { await _blobService.DeleteAsync(blobName, cancellationToken); }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not delete document blob {BlobName} after account deletion", blobName); }
            }

            // Id only -- never log the person's name/email/phone here.
            _logger.LogInformation("Account deleted and anonymized: {UserId}", userId);
            return true;
        }
    }
}
