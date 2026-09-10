using System.Text;
using System.Text.Json;

namespace ScoramAPI.Services
{
    public class Msg91VerifyResult
    {
        public bool Success { get; set; }
        // 10-digit Indian mobile number (country code stripped) once Success is true. Null otherwise.
        public string? PhoneNumber { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public interface IMsg91Service
    {
        /// <summary>Verifies an MSG91 widget access-token server-side (the widget's own client-side
        /// "success" callback is never enough on its own -- see this class's own comment) and returns
        /// the phone number MSG91 confirms was actually OTP-verified.</summary>
        Task<Msg91VerifyResult> VerifyAccessTokenAsync(string accessToken);
    }

    // MSG91's client-side widget (loaded in the browser via otp-provider.js) handles collecting the
    // phone number, sending the OTP, and letting the person enter it -- once THAT succeeds, its own
    // success(data) callback fires with a signed access-token. That token is not proof of anything by
    // itself from the server's point of view: a browser console can call the same success callback
    // with a fabricated payload, so it must be re-checked here, server-side, against MSG91's own API
    // before any account is created or any phone number is changed. This is that re-check.
    //
    // Deliberately separate from the widget's own tokenAuth/widgetId (those only authorize opening
    // the widget UI client-side) -- this uses the MSG91 account's Auth Key instead, which must never
    // reach the browser. Configured via Msg91:AuthKey (see appsettings.json); same "disabled rather
    // than a startup failure until configured" pattern as PushNotificationService's VapidKeys.
    //
    // Response shape assumption: MSG91's verifyAccessToken endpoint isn't part of their public
    // machine-readable API reference (their docs site renders it client-side, so it couldn't be
    // fetched while writing this), so this is built from MSG91's own general JSON convention used
    // across every other endpoint in their API ({"type":"success"|"error","message":"..."}), matching
    // several independent community client implementations (Ruby: otpwidget gem and MSG91's own
    // msg91-one-api gem) for this exact endpoint. On success "message" is expected to be the verified
    // identifier -- either the bare phone number or a JSON object containing one under a field like
    // "identifier"/"mobile"/"contact"; ParseVerifiedPhoneNumber below tries all of these. WORTH A
    // QUICK REAL TEST after deploying: trigger the widget once, log the raw response, and confirm it
    // actually lands in one of the shapes this parses -- if MSG91 changes their format, only this one
    // method needs updating.
    public class Msg91Service : IMsg91Service
    {
        private const string VerifyUrl = "https://control.msg91.com/api/v5/widget/verifyAccessToken";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<Msg91Service> _logger;
        private readonly string? _authKey;

        public Msg91Service(IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<Msg91Service> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _authKey = config["Msg91:AuthKey"];
        }

        public async Task<Msg91VerifyResult> VerifyAccessTokenAsync(string accessToken)
        {
            if (string.IsNullOrWhiteSpace(_authKey))
                return new Msg91VerifyResult { Success = false, ErrorMessage = "Phone verification isn't configured on the server yet." };

            if (string.IsNullOrWhiteSpace(accessToken))
                return new Msg91VerifyResult { Success = false, ErrorMessage = "Missing phone verification token." };

            var client = _httpClientFactory.CreateClient();
            // Anonymous-object initializers can't have a "access-token" property name (hyphen isn't a
            // valid C# identifier) -- a Dictionary serializes to the exact same JSON shape MSG91
            // expects, {"authkey":"...","access-token":"..."}.
            var payload = new Dictionary<string, string> { ["authkey"] = _authKey, ["access-token"] = accessToken };
            var requestBody = JsonSerializer.Serialize(payload);

            HttpResponseMessage response;
            try
            {
                response = await client.PostAsync(VerifyUrl, new StringContent(requestBody, Encoding.UTF8, "application/json"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MSG91 verifyAccessToken request failed");
                return new Msg91VerifyResult { Success = false, ErrorMessage = "Couldn't reach the phone verification service. Please try again." };
            }

            var body = await response.Content.ReadAsStringAsync();

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                _logger.LogError("MSG91 verifyAccessToken returned non-JSON response: {Body}", body);
                return new Msg91VerifyResult { Success = false, ErrorMessage = "Phone verification failed. Please try again." };
            }

            using (doc)
            {
                var root = doc.RootElement;
                var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;

                if (!string.Equals(type, "success", StringComparison.OrdinalIgnoreCase))
                {
                    var errorMessage = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : null;
                    return new Msg91VerifyResult { Success = false, ErrorMessage = errorMessage ?? "Phone verification failed. Please try again." };
                }

                var phoneNumber = ParseVerifiedPhoneNumber(root);
                if (phoneNumber == null)
                {
                    _logger.LogError("MSG91 verifyAccessToken success response didn't contain a recognizable phone number: {Body}", body);
                    return new Msg91VerifyResult { Success = false, ErrorMessage = "Couldn't confirm your phone number. Please try again." };
                }

                return new Msg91VerifyResult { Success = true, PhoneNumber = phoneNumber };
            }
        }

        // Tries every shape the "message" field has been seen to take across MSG91's own docs/SDKs for
        // this endpoint (see this class's own comment), and normalizes to a bare 10-digit number --
        // strips a leading "91" country code (India-only app; see RegisterDto.PhoneNumber's own
        // MaxLength(20), which is generous specifically because MSG91 may return it with or without
        // the code) and any non-digit formatting characters MSG91 might include.
        private static string? ParseVerifiedPhoneNumber(JsonElement root)
        {
            if (!root.TryGetProperty("message", out var messageEl)) return null;

            string? raw = null;
            if (messageEl.ValueKind == JsonValueKind.String)
            {
                raw = messageEl.GetString();
                // Some MSG91 SDKs report "message" as a JSON object serialized into this same string
                // field rather than a nested object -- try parsing it that way before giving up.
                if (raw != null && raw.TrimStart().StartsWith('{'))
                {
                    try
                    {
                        using var inner = JsonDocument.Parse(raw);
                        raw = ExtractIdentifierField(inner.RootElement);
                    }
                    catch (JsonException) { /* wasn't actually nested JSON -- use raw as-is below */ }
                }
            }
            else if (messageEl.ValueKind == JsonValueKind.Object)
            {
                raw = ExtractIdentifierField(messageEl);
            }

            if (string.IsNullOrWhiteSpace(raw)) return null;

            var digits = new string(raw.Where(char.IsDigit).ToArray());
            if (digits.Length == 12 && digits.StartsWith("91")) digits = digits[2..];
            return digits.Length == 10 ? digits : null;
        }

        private static string? ExtractIdentifierField(JsonElement obj)
        {
            foreach (var field in new[] { "identifier", "mobile", "contact", "phone", "number" })
            {
                if (obj.TryGetProperty(field, out var el) && el.ValueKind == JsonValueKind.String)
                    return el.GetString();
            }
            return null;
        }
    }
}
