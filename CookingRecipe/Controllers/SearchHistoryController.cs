using Microsoft.AspNetCore.Mvc;
using CookingRecipe.Services;
using CookingRecipe.Dtos;
using CookingRecipe.Entities;

namespace CookingRecipe.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SearchHistoryController : ControllerBase
    {
        private readonly IRedisStore _store;
        private const string CookieName = "deviceId";

        public SearchHistoryController(IRedisStore store)
        {
            _store = store;
        }

        [HttpGet]
        public async Task<IActionResult> GetHistory()
        {
            var deviceId = GetOrCreateDeviceId();
            if (string.IsNullOrEmpty(deviceId)) return BadRequest();

            var history = await _store.GetSearchHistoryAsync(deviceId);
            return Ok(history);
        }

        [HttpPost]
        public async Task<IActionResult> SaveSearch([FromBody] SaveSearchRequestDto? request)
        {
            var deviceId = GetOrCreateDeviceId();
            if (string.IsNullOrEmpty(deviceId)) return BadRequest();
            if (request is null)
            {
                return BadRequest("Request body is required.");
            }

            var history = new SearchHistory
            {
                DeviceId = deviceId,
                SearchText = request.SearchText ?? string.Empty,
                Category = request.Category ?? string.Empty,
                JsonResult = request.JsonResult ?? string.Empty,
                SearchDate = DateTime.UtcNow
            };

            await _store.SaveSearchHistoryAsync(deviceId, history);
            return Ok();
        }

        private string GetOrCreateDeviceId()
        {
            // prefer cookie
            var deviceId = Request.Cookies[CookieName];
            if (!string.IsNullOrWhiteSpace(deviceId)) return deviceId;

            // check if middleware placed it in Items
            if (HttpContext.Items.TryGetValue(CookieName, out var v) && v is string s && !string.IsNullOrWhiteSpace(s))
            {
                return s;
            }

            // fallback create and set cookie
            deviceId = Guid.NewGuid().ToString();
            Response.Cookies.Append(CookieName, deviceId, new CookieOptions
            {
                HttpOnly = false,
                IsEssential = true,
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                SameSite = IsSecureRequest() ? SameSiteMode.None : SameSiteMode.Lax,
                Secure = IsSecureRequest()
            });

            return deviceId;
        }

        private bool IsSecureRequest()
        {
            return Request.IsHttps ||
                string.Equals(Request.Headers["X-Forwarded-Proto"].FirstOrDefault(), "https", StringComparison.OrdinalIgnoreCase);
        }
    }
}
