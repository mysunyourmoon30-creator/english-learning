namespace EnglishMasterAI.Web.Configuration;

public sealed class LegalOptions
{
    public const string SectionName = "Legal";

    public string OperatorName { get; set; } = string.Empty;
    public string PrivacyContactEmail { get; set; } = string.Empty;
    public int BackupRetentionDays { get; set; }
    public int TelemetryRetentionDays { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(OperatorName)
        && !OperatorName.StartsWith("SET_", StringComparison.OrdinalIgnoreCase)
        && new System.ComponentModel.DataAnnotations.EmailAddressAttribute()
            .IsValid(PrivacyContactEmail)
        && !PrivacyContactEmail.EndsWith(
            ".invalid",
            StringComparison.OrdinalIgnoreCase)
        && BackupRetentionDays > 0
        && TelemetryRetentionDays > 0;
}
