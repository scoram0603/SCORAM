using System.Security.Cryptography;
using System.Text;

namespace ScoramAPI.Services
{
    public interface ITotpService
    {
        /// <summary>Generates a new random secret (160 bits, the RFC 4226-recommended length for
        /// HMAC-SHA1), Base32-encoded (the format every authenticator app -- Google Authenticator,
        /// Authy, 1Password, etc. -- expects, whether scanned via QR or typed in manually).</summary>
        string GenerateSecret();

        /// <summary>Builds an otpauth:// URI for a QR code. `accountLabel` (typically the admin's
        /// email) and `issuer` (e.g. "Scoram") are both shown in the authenticator app's list, so the
        /// admin can tell which entry is which if they have several accounts set up.</summary>
        string GenerateProvisioningUri(string secret, string accountLabel, string issuer);

        /// <summary>True if `code` is a valid 6-digit TOTP code for `secret` at (approximately) the
        /// current time. Checks a small window of adjacent time steps (see ValidateCode's own
        /// comment) to tolerate ordinary clock drift between the admin's phone and this server,
        /// without so wide a window that it materially weakens the code's ~30-second freshness.</summary>
        bool ValidateCode(string secret, string code);

        /// <summary>Generates a batch of one-time backup codes (for when the admin's authenticator
        /// device is lost/unavailable) -- returns the PLAINTEXT codes to show the admin exactly once,
        /// paired with their hashes for storage. Never call this a second time expecting the same
        /// codes back; only the hashes persist.</summary>
        (List<string> PlaintextCodes, List<string> Hashes) GenerateBackupCodes(int count = 10);

        /// <summary>Hashes a single backup code the same way GenerateBackupCodes does, for checking
        /// an admin-submitted code against the stored hashes at verify time.</summary>
        string HashBackupCode(string code);
    }

    public class TotpService : ITotpService
    {
        private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        private const int CodeDigits = 6;
        private static readonly TimeSpan TimeStep = TimeSpan.FromSeconds(30); // standard TOTP step, matches every authenticator app's default

        public string GenerateSecret()
        {
            var bytes = RandomNumberGenerator.GetBytes(20); // 160 bits
            return Base32Encode(bytes);
        }

        public string GenerateProvisioningUri(string secret, string accountLabel, string issuer)
        {
            // otpauth://totp/{issuer}:{accountLabel}?secret={secret}&issuer={issuer}&algorithm=SHA1&digits=6&period=30
            // SHA1/6-digits/30s are all left as their (unwritten but universal) defaults -- every
            // major authenticator app assumes them when the URI doesn't say otherwise, and spelling
            // them out explicitly risks an older app choking on a parameter it doesn't recognize.
            var label = Uri.EscapeDataString($"{issuer}:{accountLabel}");
            var encodedIssuer = Uri.EscapeDataString(issuer);
            return $"otpauth://totp/{label}?secret={secret}&issuer={encodedIssuer}";
        }

        public bool ValidateCode(string secret, string code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length != CodeDigits || !code.All(char.IsDigit))
                return false;

            var secretBytes = Base32Decode(secret);
            var currentStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / (long)TimeStep.TotalSeconds;

            // Checks the current step and one step on either side (±30s) -- tolerates the admin's
            // phone clock being slightly ahead/behind, or the code being entered right as a 30-second
            // window rolls over, without accepting anything close to stale enough to matter for
            // replay risk.
            for (var offset = -1; offset <= 1; offset++)
            {
                var candidate = ComputeCode(secretBytes, currentStep + offset);
                if (candidate == code) return true;
            }
            return false;
        }

        public (List<string> PlaintextCodes, List<string> Hashes) GenerateBackupCodes(int count = 10)
        {
            var plaintext = new List<string>(count);
            var hashes = new List<string>(count);
            for (var i = 0; i < count; i++)
            {
                // 10 random digits, grouped for readability (e.g. "1234 56789" -> shown as
                // "12345-67890") -- long enough that guessing one is infeasible (10^10 possibilities),
                // short enough to type by hand if needed.
                var codeBytes = RandomNumberGenerator.GetBytes(10);
                var code = string.Concat(codeBytes.Select(b => (b % 10).ToString()));
                plaintext.Add(code);
                hashes.Add(HashBackupCode(code));
            }
            return (plaintext, hashes);
        }

        public string HashBackupCode(string code)
        {
            // A backup code is a one-time, high-entropy (10-digit) random value, not a
            // human-chosen/reusable password -- unlike PasswordHash elsewhere (BCrypt, deliberately
            // slow to resist offline guessing of a weak, human-chosen secret), a fast SHA-256 is
            // appropriate here since brute-forcing 10^10 possibilities isn't feasible regardless of
            // hash speed, and this needs to be checked against potentially several stored codes per
            // login attempt.
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(code));
            return Convert.ToHexString(bytes);
        }

        private static string ComputeCode(byte[] secretBytes, long counter)
        {
            var counterBytes = BitConverter.GetBytes(counter);
            if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes); // RFC 4226 requires big-endian

            using var hmac = new HMACSHA1(secretBytes);
            var hash = hmac.ComputeHash(counterBytes);

            // Dynamic truncation per RFC 4226 section 5.3 -- takes 4 bytes starting at an
            // offset determined by the hash's own last nibble, then masks off the top bit.
            var offset = hash[^1] & 0x0F;
            var binaryCode = ((hash[offset] & 0x7F) << 24)
                              | ((hash[offset + 1] & 0xFF) << 16)
                              | ((hash[offset + 2] & 0xFF) << 8)
                              | (hash[offset + 3] & 0xFF);

            var code = binaryCode % (int)Math.Pow(10, CodeDigits);
            return code.ToString().PadLeft(CodeDigits, '0');
        }

        private static string Base32Encode(byte[] data)
        {
            var result = new StringBuilder((data.Length * 8 + 4) / 5);
            var buffer = 0;
            var bitsLeft = 0;
            foreach (var b in data)
            {
                buffer = (buffer << 8) | b;
                bitsLeft += 8;
                while (bitsLeft >= 5)
                {
                    bitsLeft -= 5;
                    result.Append(Base32Alphabet[(buffer >> bitsLeft) & 0x1F]);
                }
            }
            if (bitsLeft > 0)
            {
                result.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
            }
            return result.ToString();
        }

        private static byte[] Base32Decode(string base32)
        {
            base32 = base32.TrimEnd('=').ToUpperInvariant();
            var bytes = new List<byte>(base32.Length * 5 / 8);
            var buffer = 0;
            var bitsLeft = 0;
            foreach (var c in base32)
            {
                var value = Base32Alphabet.IndexOf(c);
                if (value < 0) continue; // skip any stray whitespace/formatting characters
                buffer = (buffer << 5) | value;
                bitsLeft += 5;
                if (bitsLeft >= 8)
                {
                    bitsLeft -= 8;
                    bytes.Add((byte)((buffer >> bitsLeft) & 0xFF));
                }
            }
            return bytes.ToArray();
        }
    }
}