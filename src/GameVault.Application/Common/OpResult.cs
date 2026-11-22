namespace GameVault.Application.Common;

/// <summary>Outcome of a use case that can fail for a reason the user should see.</summary>
public sealed record OpResult(bool Ok, string? Error = null)
{
    public static OpResult Success { get; } = new(true);
    public static OpResult Fail(string error) => new(false, error);
}

public sealed record OpResult<T>(bool Ok, T? Value, string? Error = null)
{
    public static OpResult<T> Success(T value) => new(true, value);
    public static OpResult<T> Fail(string error) => new(false, default, error);
}

public static class Slug
{
    /// <summary>Lower-case letters and digits separated by single hyphens; "game" if nothing usable is left.</summary>
    public static string From(string text)
    {
        var chars = text.ToLowerInvariant().Select(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '-').ToArray();
        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "game" : slug.Length > 190 ? slug[..190].TrimEnd('-') : slug;
    }
}
