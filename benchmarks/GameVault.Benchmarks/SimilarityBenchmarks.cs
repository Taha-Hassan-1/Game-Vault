using BenchmarkDotNet.Attributes;
using GameVault.Application.Catalogue;

namespace GameVault.Benchmarks;

/// <summary>
/// The only genuinely CPU-bound code in the site: ranking similar games on a cache miss. The shipped code is the "M2 pipeline".
/// The other two variants are experiments that were measured and NOT shipped, kept here so the result can be reproduced.
/// Every variant must return the same games as the pipeline, which Setup checks.
/// </summary>
[MemoryDiagnoser]
public class SimilarityBenchmarks
{
    private readonly ISimilarityStrategy _strategy = new TagOverlapSimilarity();
    private SimilarityCandidate _source = null!;
    private List<SimilarityCandidate> _pool = null!;

    [Params(200, 2000)]
    public int PoolSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var rnd = new Random(7);
        SimilarityCandidate Make(int id) => new(
            new GameCard(id, "g" + id, "Game " + id, null, null, Math.Round(rnd.NextDouble() * 5, 1), 3, false, Array.Empty<string>()),
            Enumerable.Range(0, 1 + rnd.Next(3)).Select(_ => "genre-" + rnd.Next(19)).ToHashSet(),
            Enumerable.Range(0, 5 + rnd.Next(6)).Select(_ => "tag-" + (int)(Math.Pow(rnd.NextDouble(), 2.2) * 300)).ToHashSet());

        _source = Make(0);
        _pool = Enumerable.Range(1, PoolSize).Select(Make).ToList();

        var expected = Shipped().Select(c => c.Id).ToList();
        foreach (var (name, result) in new[] { ("bounded top-N", BoundedTopN()), ("concrete sets", ConcreteSets()) })
            if (!expected.SequenceEqual(result.Select(c => c.Id)))
                throw new InvalidOperationException($"The {name} variant returns different games than the shipped pipeline.");
    }

    [Benchmark(Baseline = true, Description = "Shipped: LINQ pipeline, interface-typed sets")]
    public IReadOnlyList<GameCard> Shipped()
        => _pool
            .Select(c => (c.Card, Score: _strategy.Score(_source, c)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenByDescending(x => x.Card.RatingAverage)
            .Take(6)
            .Select(x => x.Card)
            .ToList();

    [Benchmark(Description = "Experiment: bounded top-N insertion buffer")]
    public IReadOnlyList<GameCard> BoundedTopN()
    {
        const int count = 6;
        var scores = new double[count];
        var best = new SimilarityCandidate[count];
        var found = 0;
        foreach (var candidate in _pool)
        {
            var score = _strategy.Score(_source, candidate);
            if (score <= 0) continue;
            var pos = found;
            while (pos > 0 && (score > scores[pos - 1] || (score == scores[pos - 1] && candidate.Card.RatingAverage > best[pos - 1].Card.RatingAverage))) pos--;
            if (pos >= count) continue;
            for (var j = Math.Min(found, count - 1); j > pos; j--) { scores[j] = scores[j - 1]; best[j] = best[j - 1]; }
            scores[pos] = score;
            best[pos] = candidate;
            if (found < count) found++;
        }
        return best.Take(found).Select(c => c.Card).ToList();
    }

    [Benchmark(Description = "Experiment: LINQ pipeline + Score on concrete HashSets")]
    public IReadOnlyList<GameCard> ConcreteSets()
        => _pool
            .Select(c => (c.Card, Score: ConcreteScore(_source, c)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenByDescending(x => x.Card.RatingAverage)
            .Take(6)
            .Select(x => x.Card)
            .ToList();

    // The same maths, looping over HashSet<string> directly: foreach on the interface type goes through IEnumerable<string>,
    // on the concrete type it uses HashSet's struct enumerator. Not shipped: it needs a cast that would throw for any other set type.
    private static double ConcreteScore(SimilarityCandidate a, SimilarityCandidate b)
        => TagOverlapSimilarity.GenreWeight * Jaccard((HashSet<string>)a.Genres, (HashSet<string>)b.Genres)
         + TagOverlapSimilarity.TagWeight * Jaccard((HashSet<string>)a.Tags, (HashSet<string>)b.Tags);

    private static double Jaccard(HashSet<string> x, HashSet<string> y)
    {
        if (x.Count == 0 && y.Count == 0) return 0;
        var (small, large) = x.Count <= y.Count ? (x, y) : (y, x);
        var shared = 0;
        foreach (var s in small) if (large.Contains(s)) shared++;
        var union = x.Count + y.Count - shared;
        return union == 0 ? 0 : (double)shared / union;
    }
}
