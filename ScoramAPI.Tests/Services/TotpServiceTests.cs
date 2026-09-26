using ScoramAPI.Services;
using Xunit;

namespace ScoramAPI.Tests.Services
{
    public class TotpServiceTests
    {
        private readonly TotpService _service = new();

        [Fact]
        public void GenerateSecret_ProducesOnlyValidBase32Characters()
        {
            var secret = _service.GenerateSecret();

            Assert.Matches("^[A-Z2-7]+$", secret);
        }

        [Fact]
        public void GenerateSecret_ProducesADifferentValueEachCall()
        {
            var first = _service.GenerateSecret();
            var second = _service.GenerateSecret();

            Assert.NotEqual(first, second);
        }

        [Fact]
        public void ValidateCode_AcceptsTheCurrentlyValidCodeForItsOwnSecret()
        {
            // Can't assert a specific expected digit string without either RFC 6238's test vectors
            // (see this file's own top comment on why those aren't hardcoded here) or reaching into
            // ComputeCode directly -- this instead checks the round-trip property that actually
            // matters: whatever code the service computes for "right now", it also accepts as valid
            // for "right now". A real correctness bug (wrong truncation, wrong endianness, etc.)
            // would still show up as this round-trip failing to line up with a manually-computed
            // authenticator app value during manual testing -- this test alone can't catch an
            // implementation that's internally self-consistent but wrong relative to the spec.
            var secret = _service.GenerateSecret();
            var currentCode = ComputeCurrentCodeForTest(secret);

            Assert.True(_service.ValidateCode(secret, currentCode));
        }

        [Fact]
        public void ValidateCode_RejectsAnObviouslyWrongCode()
        {
            var secret = _service.GenerateSecret();
            var currentCode = ComputeCurrentCodeForTest(secret);
            // Guaranteed different from the real code -- 6-digit codes wrap at 1,000,000.
            var wrongCode = ((int.Parse(currentCode) + 500_000) % 1_000_000).ToString("D6");

            Assert.False(_service.ValidateCode(secret, wrongCode));
        }

        [Fact]
        public void ValidateCode_RejectsACodeGeneratedForADifferentSecret()
        {
            var secretA = _service.GenerateSecret();
            var secretB = _service.GenerateSecret();
            var codeForA = ComputeCurrentCodeForTest(secretA);

            Assert.False(_service.ValidateCode(secretB, codeForA));
        }

        [Theory]
        [InlineData("12345")] // too short
        [InlineData("1234567")] // too long
        [InlineData("12345a")] // not all digits
        [InlineData("")]
        [InlineData(null)]
        public void ValidateCode_RejectsMalformedInput_WithoutThrowing(string? malformed)
        {
            var secret = _service.GenerateSecret();

            var result = _service.ValidateCode(secret, malformed!);

            Assert.False(result);
        }

        [Fact]
        public void GenerateProvisioningUri_IsAValidOtpauthUri_ContainingTheSecretAndIssuer()
        {
            var secret = _service.GenerateSecret();

            var uri = _service.GenerateProvisioningUri(secret, "admin@example.com", "Scoram");

            Assert.StartsWith("otpauth://totp/", uri);
            Assert.Contains($"secret={secret}", uri);
            Assert.Contains("issuer=Scoram", uri);
            Assert.Contains("admin%40example.com", uri); // '@' percent-encoded within the label
        }

        [Fact]
        public void GenerateBackupCodes_ProducesTheRequestedCount_AllUnique()
        {
            var (plaintext, hashes) = _service.GenerateBackupCodes(10);

            Assert.Equal(10, plaintext.Count);
            Assert.Equal(10, hashes.Count);
            Assert.Equal(10, plaintext.Distinct().Count()); // no accidental duplicates
            Assert.All(plaintext, code => Assert.Equal(10, code.Length));
            Assert.All(plaintext, code => Assert.True(code.All(char.IsDigit)));
        }

        [Fact]
        public void GenerateBackupCodes_HashesArePairedWithTheirOwnPlaintextCode_NotMixedUp()
        {
            var (plaintext, hashes) = _service.GenerateBackupCodes(10);

            for (var i = 0; i < plaintext.Count; i++)
            {
                Assert.Equal(hashes[i], _service.HashBackupCode(plaintext[i]));
            }
        }

        [Fact]
        public void HashBackupCode_IsDeterministic()
        {
            var hash1 = _service.HashBackupCode("1234567890");
            var hash2 = _service.HashBackupCode("1234567890");

            Assert.Equal(hash1, hash2);
        }

        [Fact]
        public void HashBackupCode_NeverReturnsThePlaintextCodeItself()
        {
            var code = "1234567890";
            var hash = _service.HashBackupCode(code);

            Assert.NotEqual(code, hash);
        }

        // Mirrors TotpService's private ComputeCode/Base32Decode exactly, so this test file can
        // compute "the currently-valid code" independently for the round-trip assertions above,
        // without needing those two methods made internal/public just for tests to reach them.
        private static string ComputeCurrentCodeForTest(string base32Secret)
        {
            var secretBytes = Base32DecodeForTest(base32Secret);
            var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
            var counterBytes = BitConverter.GetBytes(counter);
            if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes);

            using var hmac = new System.Security.Cryptography.HMACSHA1(secretBytes);
            var hash = hmac.ComputeHash(counterBytes);
            var offset = hash[^1] & 0x0F;
            var binaryCode = ((hash[offset] & 0x7F) << 24) | ((hash[offset + 1] & 0xFF) << 16) | ((hash[offset + 2] & 0xFF) << 8) | (hash[offset + 3] & 0xFF);
            return (binaryCode % 1_000_000).ToString("D6");
        }

        private static byte[] Base32DecodeForTest(string base32)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            var bytes = new List<byte>();
            var buffer = 0;
            var bitsLeft = 0;
            foreach (var c in base32.TrimEnd('=').ToUpperInvariant())
            {
                var value = alphabet.IndexOf(c);
                if (value < 0) continue;
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
