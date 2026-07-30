using Microsoft.AspNetCore.Identity;

namespace EnglishMasterAI.Web.Data;

// Add profile data for application users by adding properties to the ApplicationUser class
public class ApplicationUser : IdentityUser
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastActiveAt { get; set; }
    public DateTimeOffset? TermsAcceptedAt { get; set; }
    public string? TermsVersion { get; set; }
    public DateTimeOffset? PrivacyNoticeAcknowledgedAt { get; set; }
    public string? PrivacyNoticeVersion { get; set; }
}

