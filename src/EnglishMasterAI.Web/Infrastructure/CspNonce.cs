namespace EnglishMasterAI.Web.Infrastructure;

public static class CspNonce
{
    public static readonly object HttpContextItemKey = new();

    public static string Get(HttpContext context) =>
        context.Items.TryGetValue(HttpContextItemKey, out var value)
            ? value as string ?? string.Empty
            : string.Empty;
}
