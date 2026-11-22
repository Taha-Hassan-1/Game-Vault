using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;

namespace GameVault.Smoke;

/// <summary>Runs only when GAMEVAULT_SMOKE=1, so a plain `dotnet test` never needs a browser.</summary>
public sealed class SmokeFactAttribute : FactAttribute
{
    public SmokeFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("GAMEVAULT_SMOKE") != "1")
            Skip = "Browser smoke test: set GAMEVAULT_SMOKE=1 to run (needs Microsoft Edge or Chrome installed).";
    }
}

/// <summary>
/// Starts the real site as its own process on a free port with a throwaway database, and a real browser to drive it.
/// A real browser matters here because it enforces the Content Security Policy, which no HTTP-level test can.
/// </summary>
public sealed class SmokeHost : IAsyncLifetime
{
    private static readonly string Server = Environment.GetEnvironmentVariable("GAMEVAULT_TEST_SERVER") ?? @".\SQLEXPRESS";
    private readonly string _db = "GameVault_Smoke_" + Guid.NewGuid().ToString("N");
    private readonly string _uploads = Path.Combine(Path.GetTempPath(), "gv_smoke_" + Guid.NewGuid().ToString("N"));
    private Process? _app;
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;
    public string BaseUrl { get; private set; } = "";

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("GAMEVAULT_SMOKE") != "1") return;

        var port = FreePort();
        BaseUrl = $"http://127.0.0.1:{port}";
        var webProject = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GameVault.Web"));

        var psi = new ProcessStartInfo("dotnet", $"run --project \"{webProject}\" --no-build --urls {BaseUrl}")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        psi.Environment["ConnectionStrings__GameVault"] =
            $"Server={Server};Database={_db};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";
        psi.Environment["Uploads__Path"] = _uploads;
        _app = Process.Start(psi)!;
        _app.OutputDataReceived += (_, _) => { };
        _app.ErrorDataReceived += (_, _) => { };
        _app.BeginOutputReadLine();
        _app.BeginErrorReadLine();

        await WaitForHealthyAsync();

        _playwright = await Playwright.CreateAsync();
        // The installed Edge is used, so no browser has to be downloaded.
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = "msedge", Headless = true });
    }

    private async Task WaitForHealthyAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            if (_app!.HasExited) throw new InvalidOperationException("The site exited while starting.");
            try
            {
                if ((await http.GetAsync(BaseUrl + "/health")).StatusCode == HttpStatusCode.OK) return;
            }
            catch (Exception) { /* not up yet */ }
            await Task.Delay(500);
        }
        throw new TimeoutException("The site did not become healthy within 90 seconds.");
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null) await Browser.CloseAsync();
        _playwright?.Dispose();
        if (_app is { HasExited: false }) _app.Kill(entireProcessTree: true);
        _app?.WaitForExit(10_000);

        try
        {
            using var cn = new SqlConnection($"Server={Server};Database=master;Trusted_Connection=True;TrustServerCertificate=True");
            await cn.OpenAsync();
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = $"IF DB_ID('{_db}') IS NOT NULL BEGIN ALTER DATABASE [{_db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_db}]; END";
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception) { /* best effort: a leftover GameVault_Smoke_* database is harmless */ }
        if (Directory.Exists(_uploads)) Directory.Delete(_uploads, recursive: true);
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}

[CollectionDefinition("Smoke")]
public sealed class SmokeCollection : ICollectionFixture<SmokeHost> { }
