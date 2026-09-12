using Microsoft.AspNetCore.Mvc;
using ScoramAPI.DTOs;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // Deliberately unauthenticated -- a captcha challenge has to be fetchable BEFORE the student has
    // proven anything (it's part of what proves they're not a bot on Register/Login in the first
    // place). See CaptchaService's own comment for what this is and isn't meant to stop.
    [ApiController]
    [Route("api/captcha")]
    public class CaptchaController : ControllerBase
    {
        private readonly ICaptchaService _captcha;

        public CaptchaController(ICaptchaService captcha)
        {
            _captcha = captcha;
        }

        // GET /api/captcha/generate -- called once when the Register or Login (password) screen
        // loads, and again any time the student wants a new question (e.g. they got it wrong, or
        // just want to refresh it) -- each call issues a brand new CaptchaId/answer pair.
        [HttpGet("generate")]
        public ActionResult<CaptchaChallengeDto> Generate()
        {
            var (captchaId, question) = _captcha.Generate();
            return Ok(new CaptchaChallengeDto { CaptchaId = captchaId, Question = question });
        }
    }
}
