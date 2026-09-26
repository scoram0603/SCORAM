using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    public interface ITokenService
    {
        (string token, DateTime expiresAt) GenerateToken(User user);
        (string token, DateTime expiresAt) GenerateAdminToken(Admin admin);

        // Raw refresh tokens are handed to the client and never stored -- only HashRefreshToken's
        // digest of them lives in the RefreshTokens table (see that model's own comment on why).
        (string rawToken, DateTime expiresAt) GenerateRefreshToken();
        string HashRefreshToken(string rawToken);
    }

    public class TokenService : ITokenService
    {
        private readonly IConfiguration _config;

        public TokenService(IConfiguration config)
        {
            _config = config;
        }

        public (string token, DateTime expiresAt) GenerateToken(User user)
        {
            var jwtSection = _config.GetSection("Jwt");
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var expiryMinutes = int.Parse(jwtSection["ExpiryMinutes"] ?? "60");
            var expiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Role, "Student"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                // Checked against User.SecurityStamp on every authenticated request (see
                // SecurityStampService and Program.cs's OnTokenValidated) -- this is what lets a
                // password change, logout, or deactivation invalidate an access token that hasn't
                // expired yet, despite JWTs otherwise being stateless/unrevokable once issued.
                new Claim("stamp", user.SecurityStamp.ToString())
            };

            var token = new JwtSecurityToken(
                issuer: jwtSection["Issuer"],
                audience: jwtSection["Audience"],
                claims: claims,
                expires: expiresAt,
                signingCredentials: credentials
            );

            return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
        }

        public (string token, DateTime expiresAt) GenerateAdminToken(Admin admin)
        {
            var jwtSection = _config.GetSection("Jwt");
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var expiryMinutes = int.Parse(jwtSection["ExpiryMinutes"] ?? "60");
            var expiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes);

            // Role claim is the admin's real role (Admin / SuperAdmin), never "Student" -- this is what
            // lets [Authorize(Roles = "Admin,SuperAdmin")] and [Authorize(Roles = "Student")] tell a
            // student token and an admin token apart even though both carry a "sub" id claim.
            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, admin.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, admin.Email),
                new Claim(ClaimTypes.Name, admin.FullName),
                new Claim(ClaimTypes.Role, admin.Role.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                // Read by the enforcement filter in Program.cs to block every admin action except
                // change-password while true. Baked in at login time rather than checked against the
                // DB on every request, same tradeoff as the Role claim above -- if it changes
                // mid-token-lifetime (i.e. right after a successful password change), the frontend is
                // expected to discard this token and log in again for a clean one.
                new Claim("mustChangePassword", admin.MustChangePassword.ToString()),
                // See the matching comment on GenerateToken above -- same mechanism, Admin.SecurityStamp.
                new Claim("stamp", admin.SecurityStamp.ToString())
            };

            var token = new JwtSecurityToken(
                issuer: jwtSection["Issuer"],
                audience: jwtSection["Audience"],
                claims: claims,
                expires: expiresAt,
                signingCredentials: credentials
            );

            return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
        }

        public (string rawToken, DateTime expiresAt) GenerateRefreshToken()
        {
            var jwtSection = _config.GetSection("Jwt");
            var refreshDays = int.Parse(jwtSection["RefreshExpiryDays"] ?? "30");

            // 256 bits of randomness from a CSPRNG -- same bar as a session token, since this is
            // effectively a long-lived credential (it's what lets a client keep getting new access
            // tokens without re-entering a password).
            var bytes = RandomNumberGenerator.GetBytes(32);
            var rawToken = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

            return (rawToken, DateTime.UtcNow.AddDays(refreshDays));
        }

        public string HashRefreshToken(string rawToken)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
            return Convert.ToHexString(bytes);
        }
    }
}
