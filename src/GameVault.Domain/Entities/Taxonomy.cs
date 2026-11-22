namespace GameVault.Domain.Entities;

public abstract class Taxon
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public List<Game> Games { get; set; } = new();
}

public class Genre : Taxon { }
public class Platform : Taxon { }
public class Tag : Taxon { }
