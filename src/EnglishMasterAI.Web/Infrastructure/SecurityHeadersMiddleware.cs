using System.Security.Cryptography;

namespace EnglishMasterAI.Web.Infrastructure;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        context.Items[CspNonce.HttpContextItemKey] = nonce;

        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), geolocation=(), microphone=(self)";
        headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "base-uri 'self'; frame-ancestors 'none'; object-src 'none'; " +
            "img-src 'self' data:; media-src 'self' blob:; " +
            $"style-src 'self' 'unsafe-inline'; script-src 'self' 'nonce-{nonce}'; " +
            "connect-src 'self' ws: wss:";

        return next(context);
    }
}
