namespace EnglishMasterAI.E2E;

public sealed class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("E2E_BASE_URL")))
        {
            Skip = "E2E_BASE_URL is not configured; no browser journey was executed.";
        }
    }
}
