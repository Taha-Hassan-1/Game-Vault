namespace GameVault.Domain.Entities;

/// <summary>Outbound requests made to a metadata provider on one day; the budget guard sums these per month.</summary>
public class ApiUsage
{
    public DateOnly Date { get; set; }
    public string Provider { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>Small key/value store for background-job cursors, such as the next RAWG page to import.</summary>
public class SyncState
{
    public string Key { get; set; } = "";
    public int Value { get; set; }
}
