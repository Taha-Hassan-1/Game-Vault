namespace GameVault.Web.Security;

public static class Policies
{
    public const string CanModerate = "CanModerate";
    public const string StudioOwner = "StudioOwner";
}

public static class UserExtensions
{
    public static string? UserId(this System.Security.Claims.ClaimsPrincipal user)
        => user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
}
