namespace ScoramAPI.Services
{
    // Stored in IStagedDataCache keyed by a random opaque token (see AdminAuthController's
    // MfaChallengePrefix), never sent to or received from the client directly -- the client only
    // ever handles the opaque token string itself.
    public class MfaChallenge
    {
        public Guid AdminId { get; set; }
        // Wrong codes tried against this challenge -- capped so one challenge can't be used to brute-force the 6-digit code.
        public int FailedAttempts { get; set; }
    }
}
