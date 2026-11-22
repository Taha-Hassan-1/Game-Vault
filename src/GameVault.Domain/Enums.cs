namespace GameVault.Domain;

public enum GameSource { Rawg = 0, Studio = 1 }
public enum ListKind { Wishlist = 0, Played = 1 }
public enum SuggestionStatus { New = 0, Planned = 1, InProgress = 2, Shipped = 3, Declined = 4 }
public enum FeedbackSource { Web = 0, Api = 1 }
public enum FeedbackCategory { Bug = 0, Idea = 1, Praise = 2 }
public enum FeedbackStatus { New = 0, Read = 1, Archived = 2 }

public static class Roles
{
    public const string Member = "Member";
    public const string Moderator = "Moderator";
    public const string Admin = "Admin";
}
