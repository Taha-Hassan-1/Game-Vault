using System.Diagnostics;
using System.Net;

namespace GameVault.Benchmarks;

/// <summary>
/// A small closed-loop load generator: N workers send requests back to back and every latency is recorded.
/// It stands in for k6 when k6 is not installed; perf/catalogue.k6.js does the same against a deployed site.
/// </summary>
public static class LoadRunner
{
    public static async Task RunAsync(string baseUrl, string[] paths, int requests, int concurrency, string encoding)
    {
        // No automatic decompression, so the byte counts are what actually crosses the wire.
        using var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.None, AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
        if (encoding != "identity") http.DefaultRequestHeaders.AcceptEncoding.ParseAdd(encoding);

        // The first request to each page is the cold one (cache miss, JIT, connection setup). It is reported, then not counted.
        Console.WriteLine("first (cold) request per page, ms:");
        foreach (var p in paths)
        {
            var cold = Stopwatch.StartNew();
            using var r = await http.GetAsync(p);
            var size = (await r.Content.ReadAsByteArrayAsync()).Length;
            Console.WriteLine($"  {cold.Elapsed.TotalMilliseconds,7:F1}  {(int)r.StatusCode}  {size,7} B  {p}");
        }
        foreach (var p in paths) await http.GetAsync(p); // second pass: everything the steady state needs is warm

        var latencies = new List<double>(requests);
        var statuses = new Dictionary<int, int>();
        long bytes = 0;
        var next = 0;
        var gate = new object();
        var wall = Stopwatch.StartNew();

        await Task.WhenAll(Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
        {
            while (true)
            {
                int n;
                lock (gate) { if (next >= requests) return; n = next++; }
                var sw = Stopwatch.StartNew();
                using var r = await http.GetAsync(paths[n % paths.Length]);
                var body = await r.Content.ReadAsByteArrayAsync();
                sw.Stop();
                lock (gate)
                {
                    latencies.Add(sw.Elapsed.TotalMilliseconds);
                    bytes += body.Length;
                    statuses[(int)r.StatusCode] = statuses.GetValueOrDefault((int)r.StatusCode) + 1;
                }
            }
        })));
        wall.Stop();

        latencies.Sort();
        double P(double q) => latencies[Math.Min(latencies.Count - 1, (int)(latencies.Count * q))];
        Console.WriteLine($"{string.Join(", ", paths)}");
        Console.WriteLine($"requests {latencies.Count}  concurrency {concurrency}  encoding {encoding}  wall {wall.Elapsed.TotalSeconds:F1}s  rps {latencies.Count / wall.Elapsed.TotalSeconds:F0}");
        Console.WriteLine($"latency ms  p50 {P(0.50):F1}  p90 {P(0.90):F1}  p95 {P(0.95):F1}  p99 {P(0.99):F1}  max {latencies[^1]:F1}");
        Console.WriteLine($"avg body {bytes / latencies.Count / 1024.0:F1} KB   statuses {string.Join(" ", statuses.Select(s => $"{s.Key}x{s.Value}"))}");
    }
}
