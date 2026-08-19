using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Extensions;

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
    }
}
