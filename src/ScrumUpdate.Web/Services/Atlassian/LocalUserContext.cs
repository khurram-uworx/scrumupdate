using System.Security.Cryptography;

namespace ScrumUpdate.Web.Services.Atlassian;

public sealed class LocalUserContext
{
    const string UserCookieName = "scrumupdate_user";

    public string GetOrCreateLocalUserId(HttpContext httpContext)
    {
        if (httpContext.Request.Cookies.TryGetValue(UserCookieName, out var cookieValue) && !string.IsNullOrWhiteSpace(cookieValue))
        {
            return cookieValue;
        }

        var newUserId = Guid.NewGuid().ToString("N");
        httpContext.Response.Cookies.Append(UserCookieName, newUserId, new CookieOptions
        {
            HttpOnly = true,
            Secure = httpContext.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromDays(90)
        });

        return newUserId;
    }

    public static string CreateSecureRandomToken(int bytes = 32)
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
