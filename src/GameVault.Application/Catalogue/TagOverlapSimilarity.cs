namespace GameVault.Application.Catalogue;

/// <summary>Weighted Jaccard overlap: genres count more than tags because they are coarser and more reliable.</summary>
public sealed class TagOverlapSimilarity : ISimilarityStrategy
{
    public const double GenreWeight = 0.6;
    public const double TagWeight = 0.4;

    public double Score(SimilarityCandidate source, SimilarityCandidate other)
        => GenreWeight * Jaccard(source.Genres, other.Genres) + TagWeight * Jaccard(source.Tags, other.Tags);

    internal static double Jaccard(IReadOnlySet<string> a, IReadOnlySet<string> b)
    {
        if (a.Count == 0 && b.Count == 0) return 0;
        var shared = 0;
        foreach (var x in a) if (b.Contains(x)) shared++;
        var union = a.Count + b.Count - shared;
        return union == 0 ? 0 : (double)shared / union;
    }
}
