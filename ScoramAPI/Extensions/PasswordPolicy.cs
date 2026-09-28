namespace ScoramAPI.Extensions
{
    // Server-side password policy for all admin accounts.
    // NEVER rely solely on client-side validation -- these checks are the real security boundary.
    // The same policy applies both to the forced change-password flow (SuperAdminBootstrapService
    // sets MustChangePassword = true on fresh accounts) and to voluntary password changes.
    public static class PasswordPolicy
    {
        public const int MinLength = 12;

        // Returns null on success, or a user-facing error message on failure.
        // The message is intentionally generic (never reveals which specific rule failed first)
        // because listing exactly which requirement failed leaks information useful to an attacker.
        // The UI already shows all requirements up front, so "doesn't meet policy" is enough.
        public static string? Validate(string password)
        {
            if (string.IsNullOrEmpty(password) || password.Length < MinLength)
                return $"Password must be at least {MinLength} characters.";

            if (!password.Any(char.IsUpper))
                return "Password must contain at least one uppercase letter.";

            if (!password.Any(char.IsLower))
                return "Password must contain at least one lowercase letter.";

            if (!password.Any(char.IsDigit))
                return "Password must contain at least one number.";

            if (!password.Any(c => !char.IsLetterOrDigit(c)))
                return "Password must contain at least one special character.";

            return null; // valid
        }
    }
}
