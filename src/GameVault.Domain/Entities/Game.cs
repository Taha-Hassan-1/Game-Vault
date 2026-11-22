namespace GameVault.Domain.Entities;

public class Game
{
    public const int MaxScreenshots = 8;

    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public DateOnly? ReleaseDate { get; set; }
    public string? CoverUrl { get; set; }
    public GameSource Source { get; set; }
    public string? ExternalId { get; set; }
    public bool IsStudioGame { get; set; }
    public double RatingAverage { get; set; }
    public int RatingCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExternalFetchedAt { get; set; }

    /// <summary>When description and screenshots were last fetched from the provider; null until a game page is first opened.</summary>
    public DateTime? DetailsFetchedAt { get; set; }

    public List<Genre> Genres { get; set; } = new();
    public List<Platform> Platforms { get; set; } = new();
    public List<Tag> Tags { get; set; } = new();
    public List<Screenshot> Screenshots { get; set; } = new();
    public List<Review> Reviews { get; set; } = new();

    public void ApplyRating(int count, double average)
    {
        RatingCount = count;
        RatingAverage = count == 0 ? 0 : Math.Round(average, 2);
    }

    public bool CanAddScreenshot() => Screenshots.Count < MaxScreenshots;
}

public class Screenshot
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public string Url { get; set; } = "";
}
