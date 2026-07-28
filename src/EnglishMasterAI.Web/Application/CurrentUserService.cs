using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace EnglishMasterAI.Web.Application;

public sealed class CurrentUserService(AuthenticationStateProvider authenticationStateProvider)
{
    public async Task<string> GetRequiredUserIdAsync()
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        return state.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("This action requires an authenticated user.");
    }
}
