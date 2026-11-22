using GameVault.Benchmarks;

// Performance tooling for GameVault. See docs/PERFORMANCE.md for what each command measures and the recorded results.
//
//   seed [games]                                            recreate GameVault_Perf with a synthetic catalogue (default 5000)
//   queries [label]                                         time the query variants against GameVault_Perf
//   load <url> <requests> <concurrency> [gzip|br] <path>... closed-loop load against a running site
//   bdn                                                     BenchmarkDotNet: the similarity hot path
switch (args.FirstOrDefault())
{
    case "seed":
        PerfDb.Seed(args.Length > 1 ? int.Parse(args[1]) : 5000);
        return 0;
    case "migrate":
    {
        // Applies pending migrations to the perf database only, so the same catalogue can be measured before and after a schema change.
        await using var db = new GameVault.Infrastructure.Persistence.GameVaultDbContext(PerfDb.Options());
        await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.MigrateAsync(db.Database);
        Console.WriteLine("Migrated " + PerfDb.Name);
        return 0;
    }
    case "queries":
        await QueryLab.RunAsync(args.Length > 1 ? args[1] : "unlabelled");
        return 0;
    case "load":
    {
        var encoding = args.Contains("br") ? "br" : args.Contains("gzip") ? "gzip" : "identity";
        var paths = args.Skip(4).Where(a => a is not ("gzip" or "br")).ToArray();
        await LoadRunner.RunAsync(args[1], paths, int.Parse(args[2]), int.Parse(args[3]), encoding);
        return 0;
    }
    case "bdn":
        BenchmarkDotNet.Running.BenchmarkRunner.Run<SimilarityBenchmarks>(args: args.Skip(1).ToArray());
        return 0;
    default:
        Console.WriteLine("Commands: seed [games] | queries [label] | load <url> <requests> <concurrency> [gzip] <path>... | bdn");
        return 1;
}
