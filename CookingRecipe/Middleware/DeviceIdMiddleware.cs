using Microsoft.AspNetCore.Http;

namespace CookingRecipe.Middleware
{
    public class DeviceIdMiddleware
    {
        private readonly RequestDelegate _next;
        private const string CookieName = "deviceId";

        public DeviceIdMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var deviceId = context.Request.Cookies[CookieName];
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                deviceId = Guid.NewGuid().ToString();
                context.Response.Cookies.Append(CookieName, deviceId, BuildDeviceCookieOptions(context.Request));
            }

            // expose device id to downstream components
            context.Items[CookieName] = deviceId;

            await _next(context);
        }

        private static CookieOptions BuildDeviceCookieOptions(HttpRequest request)
        {
            var isSecureRequest = request.IsHttps ||
                string.Equals(request.Headers["X-Forwarded-Proto"].FirstOrDefault(), "https", StringComparison.OrdinalIgnoreCase);

            return new CookieOptions
            {
                HttpOnly = false,
                IsEssential = true,
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                SameSite = isSecureRequest ? SameSiteMode.None : SameSiteMode.Lax,
                Secure = isSecureRequest
            };
        }
    }
}
