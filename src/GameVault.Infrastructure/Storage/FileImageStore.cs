using GameVault.Application.Studio;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GameVault.Infrastructure.Storage;

public sealed class UploadsOptions
{
    public const string Section = "Uploads";

    /// <summary>Folder for uploaded images. Empty means App_Data/uploads under the content root.</summary>
    public string Path { get; set; } = "";
}

/// <summary>Writes uploads to a local folder that the web app serves at /uploads. Swappable for blob storage behind IImageStore.</summary>
public sealed class FileImageStore : IImageStore
{
    public const string RequestPath = "/uploads";

    private readonly string _root;

    public FileImageStore(IOptions<UploadsOptions> options, IHostEnvironment env)
    {
        _root = ResolveRoot(options.Value, env);
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    public static string ResolveRoot(UploadsOptions options, IHostEnvironment env)
        => System.IO.Path.GetFullPath(string.IsNullOrWhiteSpace(options.Path)
            ? System.IO.Path.Combine(env.ContentRootPath, "App_Data", "uploads")
            : options.Path);

    public async Task<string> SaveAsync(byte[] content, string extension, CancellationToken ct = default)
    {
        // The name is generated here; nothing the user supplied ever becomes part of a path.
        var name = Guid.NewGuid().ToString("N") + extension;
        await File.WriteAllBytesAsync(System.IO.Path.Combine(_root, name), content, ct);
        return $"{RequestPath}/{name}";
    }

    public Task DeleteAsync(string url, CancellationToken ct = default)
    {
        var prefix = RequestPath + "/";
        if (!url.StartsWith(prefix, StringComparison.Ordinal)) return Task.CompletedTask;

        var name = url[prefix.Length..];
        // Only plain file names we generated: no separators, no dots beyond the extension.
        if (name.Length == 0 || name != System.IO.Path.GetFileName(name) || name.Contains("..")) return Task.CompletedTask;

        var path = System.IO.Path.Combine(_root, name);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
}
