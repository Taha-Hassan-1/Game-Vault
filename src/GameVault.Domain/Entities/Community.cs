namespace GameVault.Domain.Entities;

public class Review
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public Game? Game { get; set; }
    public string UserId { get; set; } = "";
    public int Stars { get; private set; }
    public string Body { get; private set; } = "";
    public bool IsHidden { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EditedAt { get; set; }

    public void Write(int stars, string body)
    {
        if (stars is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(stars), "Stars must be 1 to 5.");
        Stars = stars;
        Body = (body ?? "").Trim();
    }
}

/// <summary>A member's report on a review; one per member per review. Moderators clear them when they act.</summary>
public class ReviewReport
{
    public int ReviewId { get; set; }
    public string UserId { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ListEntry
{
    public string UserId { get; set; } = "";
    public int GameId { get; set; }
    public Game? Game { get; set; }
    public ListKind Kind { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}

public class Suggestion
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public Game? Game { get; set; }
    public string AuthorId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public SuggestionStatus Status { get; set; } = SuggestionStatus.New;
    public int VoteCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<SuggestionVote> Votes { get; set; } = new();
}

public class SuggestionVote
{
    public int SuggestionId { get; set; }
    public string UserId { get; set; } = "";
}

public class FeedbackMessage
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public Game? Game { get; set; }
    public FeedbackSource Source { get; set; }
    public FeedbackCategory Category { get; set; }
    public string Body { get; set; } = "";
    public string? PlayerRef { get; set; }
    public string? AppVersion { get; set; }
    public FeedbackStatus Status { get; set; } = FeedbackStatus.New;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ApiKey
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public Game? Game { get; set; }
    public string KeyPrefix { get; set; } = "";
    public string KeyHash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
    public bool IsActive => RevokedAt is null;
}
