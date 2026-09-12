namespace ScoramAPI.DTOs
{
    public class CaptchaChallengeDto
    {
        public string CaptchaId { get; set; } = string.Empty;

        // A plain question string ("7 + 4 = ?") -- see CaptchaService's own comment on why this is
        // a math question rather than a distorted-text image. Rendered as-is by the client.
        public string Question { get; set; } = string.Empty;
    }
}
