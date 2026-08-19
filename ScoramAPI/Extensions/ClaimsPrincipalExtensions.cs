using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace ScoramAPI.Extensions
{
    public static class ClaimsPrincipalExtensions
    {
        /// <summary>
        /// Reads the authenticated user's id from the JWT "sub" claim.
        /// Checks both the raw "sub" claim type and ClaimTypes.NameIdentifier, since
        /// ASP.NET Core's inbound claim-type mapping behavior for JwtBearer has changed
        /// across versions (some versions remap "sub" -> ClaimTypes.NameIdentifier
        /// automatically, others preserve "sub" as-is).
        /// </summary>
        public static Guid GetUserId(this ClaimsPrincipal user)
        {
            var raw = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
                      ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

            if (raw == null || !Guid.TryParse(raw, out var userId))
                throw new InvalidOperationException("No valid user id claim found on the authenticated principal.");

            return userId;
        }

        /// <summary>
        /// Reads the authenticated admin's id from the JWT "sub" claim. Same underlying claim as
        /// GetUserId() -- this is just a semantically clearer name to use inside admin-only endpoints
        /// (which are guarded by [Authorize(Roles = "Admin,SuperAdmin")], so by the time this runs the
        /// caller is already known to be an admin, not a student).
        /// </summary>
        public static Guid GetAdminId(this ClaimsPrincipal user) => user.GetUserId();
    }
}
