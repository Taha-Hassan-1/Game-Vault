namespace GameVault.Application.Common;

/// <summary>Outcome of a use case that can fail for a reason the user should see.</summary>
public sealed record OpResult(bool Ok, string? Error = null)
{
    public static OpResult Success { get; } = new(true);
    public static OpResult Fail(string error) => new(false, error);
}
