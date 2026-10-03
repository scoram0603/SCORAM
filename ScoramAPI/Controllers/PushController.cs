using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Extensions;
using ScoramAPI.Models;

namespace ScoramAPI.Controllers
{
    [ApiController]
    [Route("api/push")]
    public class PushController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly IConfiguration _config;

        public PushController(ScoramDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        // GET /api/push/vapid-public-key -- public by design (it's, well, public). The frontend
        // fetches this once before calling PushManager.subscribe() rather than hardcoding it in the
        // built JS bundle, so rotating keys server-side doesn't require a frontend redeploy.
        [HttpGet("vapid-public-key")]
        public ActionResult<VapidPublicKeyDto> GetVapidPublicKey()
        {
            var publicKey = _config["VapidKeys:PublicKey"];
            if (string.IsNullOrWhiteSpace(publicKey))
                return NotFound(new { message = "Push notifications aren't configured on this server yet." });

            return Ok(new VapidPublicKeyDto { PublicKey = publicKey });
        }

        [Authorize(Roles = "Student")]
        [HttpPost("subscribe")]
        public async Task<IActionResult> Subscribe(PushSubscribeDto dto)
        {
            var userId = User.GetUserId();

            var existing = await _db.PushSubscriptions.FirstOrDefaultAsync(p => p.Endpoint == dto.Endpoint);
            if (existing != null)
            {
                // Same browser endpoint re-subscribing -- e.g. keys rotated client-side, or it was
                // previously tied to a different account on a shared machine. Re-point it.
                existing.UserId = userId;
                existing.P256dh = dto.P256dh;
                existing.Auth = dto.Auth;
            }
            else
            {
                _db.PushSubscriptions.Add(new Models.PushSubscription
                {
                    UserId = userId,
                    Endpoint = dto.Endpoint,
                    P256dh = dto.P256dh,
                    Auth = dto.Auth
                });
            }

            await _db.SaveChangesAsync();
            return NoContent();
        }

        [Authorize(Roles = "Student")]
        [HttpPost("unsubscribe")]
        public async Task<IActionResult> Unsubscribe(PushUnsubscribeDto dto)
        {
            var userId = User.GetUserId();
            var existing = await _db.PushSubscriptions.FirstOrDefaultAsync(p => p.Endpoint == dto.Endpoint && p.UserId == userId);
            if (existing != null)
            {
                _db.PushSubscriptions.Remove(existing);
                await _db.SaveChangesAsync();
            }
            return NoContent();
        }

        // MOBILE PUSH (Firebase Cloud Messaging) -- the app calls this after login and whenever FCM
        // hands it a refreshed token. IDEMPOTENT and ownership-safe:
        //  * UserId always comes from the JWT, never the request body.
        //  * The same token registered again (retry, token refresh, app restart) updates the one row;
        //    it never creates a second one. A unique index on Token backs this up at the DB level, so
        //    even two concurrent calls cannot produce duplicates (the loser retries as an update).
        //  * A token already owned by ANOTHER account is re-pointed to the caller -- the device is now
        //    signed in as them, so the previous account must stop receiving its pushes (account switch).
        //  * A previously deactivated token is reactivated.
        [Authorize(Roles = "Student")]
        [HttpPost("register-device")]
        public async Task<IActionResult> RegisterDevice(RegisterDeviceDto dto)
        {
            var userId = User.GetUserId();
            var token = dto.Token.Trim();
            if (token.Length == 0) return BadRequest(new { message = "Token is required." });
            var platform = string.IsNullOrWhiteSpace(dto.Platform) ? "Android" : dto.Platform.Trim();

            for (var attempt = 0; attempt < 2; attempt++)
            {
                var existing = await _db.DeviceTokens.FirstOrDefaultAsync(d => d.Token == token);
                var now = DateTime.UtcNow;
                if (existing != null)
                {
                    existing.UserId = userId;
                    existing.Platform = platform;
                    existing.AppVersion = dto.AppVersion;
                    existing.IsActive = true;
                    existing.LastUsedAt = now;
                    existing.UpdatedAt = now;
                }
                else
                {
                    _db.DeviceTokens.Add(new DeviceToken
                    {
                        UserId = userId,
                        Token = token,
                        Platform = platform,
                        AppVersion = dto.AppVersion,
                        IsActive = true,
                        LastUsedAt = now,
                        UpdatedAt = now
                    });
                }

                try
                {
                    await _db.SaveChangesAsync();
                    return NoContent();
                }
                catch (DbUpdateException) when (attempt == 0)
                {
                    // Concurrent insert of the same token won the unique index -- clear this context's
                    // pending state and redo as an update.
                    _db.ChangeTracker.Clear();
                }
            }
            return NoContent();
        }

        // Called on explicit logout (BEFORE the session is cleared -- it needs the JWT) so a shared/reused
        // device stops getting pushes meant for the account that just signed out. Only removes the row if
        // the caller owns it; unknown tokens are a quiet no-op so the call is safe to repeat.
        [Authorize(Roles = "Student")]
        [HttpPost("unregister-device")]
        public async Task<IActionResult> UnregisterDevice(UnregisterDeviceDto dto)
        {
            var userId = User.GetUserId();
            var token = dto.Token.Trim();
            var existing = await _db.DeviceTokens.FirstOrDefaultAsync(d => d.Token == token && d.UserId == userId);
            if (existing != null)
            {
                _db.DeviceTokens.Remove(existing);
                await _db.SaveChangesAsync();
            }
            return NoContent();
        }
    }
}
