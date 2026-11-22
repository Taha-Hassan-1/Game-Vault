using GameVault.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Infrastructure.Persistence;

public class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
}

public class GameVaultDbContext : IdentityDbContext<AppUser>
{
    public GameVaultDbContext(DbContextOptions<GameVaultDbContext> options) : base(options) { }

    public DbSet<Game> Games => Set<Game>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<Platform> Platforms => Set<Platform>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Screenshot> Screenshots => Set<Screenshot>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<ReviewReport> ReviewReports => Set<ReviewReport>();
    public DbSet<ListEntry> ListEntries => Set<ListEntry>();
    public DbSet<Suggestion> Suggestions => Set<Suggestion>();
    public DbSet<SuggestionVote> SuggestionVotes => Set<SuggestionVote>();
    public DbSet<FeedbackMessage> FeedbackMessages => Set<FeedbackMessage>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<ApiUsage> ApiUsage => Set<ApiUsage>();
    public DbSet<SyncState> SyncStates => Set<SyncState>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<AppUser>().Property(u => u.DisplayName).HasMaxLength(50);

        b.Entity<Game>(e =>
        {
            e.Property(x => x.Slug).HasMaxLength(200).IsRequired();
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.CoverUrl).HasMaxLength(500);
            e.Property(x => x.ExternalId).HasMaxLength(64);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => new { x.Source, x.ExternalId }).IsUnique().HasFilter("[ExternalId] IS NOT NULL");
            e.HasIndex(x => x.Title);
            // One index per catalogue sort, with the same key order as the ORDER BY (tie-break on Title included) so the first
            // page is read straight off the index instead of scanning and sorting every game. Measured in docs/PERFORMANCE.md.
            e.HasIndex(x => new { x.CreatedAt, x.Title }).IsDescending(true, false).HasDatabaseName("IX_Games_CreatedAt_Title");
            e.HasIndex(x => new { x.ReleaseDate, x.Title }).IsDescending(true, false).HasDatabaseName("IX_Games_ReleaseDate_Title");
            e.HasIndex(x => new { x.RatingAverage, x.RatingCount, x.Title }).IsDescending(true, true, false).HasDatabaseName("IX_Games_Rating_Title");
            e.HasMany(x => x.Genres).WithMany(x => x.Games).UsingEntity("GameGenre");
            e.HasMany(x => x.Platforms).WithMany(x => x.Games).UsingEntity("GamePlatform");
            e.HasMany(x => x.Tags).WithMany(x => x.Games).UsingEntity("GameTag");
            e.HasMany(x => x.Screenshots).WithOne().HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Cascade);
        });

        foreach (var type in new[] { typeof(Genre), typeof(Platform), typeof(Tag) })
        {
            b.Entity(type, e =>
            {
                e.Property(nameof(Taxon.Name)).HasMaxLength(100).IsRequired();
                e.Property(nameof(Taxon.Slug)).HasMaxLength(100).IsRequired();
                e.HasIndex(nameof(Taxon.Slug)).IsUnique();
            });
        }

        b.Entity<Screenshot>().Property(x => x.Url).HasMaxLength(500).IsRequired();

        b.Entity<Review>(e =>
        {
            e.Property(x => x.Body).HasMaxLength(4000);
            e.HasIndex(x => new { x.GameId, x.UserId }).IsUnique();
            e.HasOne(x => x.Game).WithMany(x => x.Reviews).HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<ReviewReport>(e =>
        {
            e.HasKey(x => new { x.ReviewId, x.UserId });
            e.Property(x => x.Reason).HasMaxLength(200).IsRequired();
            e.HasOne<Review>().WithMany().HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<ListEntry>(e =>
        {
            e.HasKey(x => new { x.UserId, x.GameId, x.Kind });
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
            e.HasOne(x => x.Game).WithMany().HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Suggestion>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Body).HasMaxLength(4000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.HasOne(x => x.Game).WithMany().HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(x => x.Votes).WithOne().HasForeignKey(x => x.SuggestionId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<SuggestionVote>(e =>
        {
            e.HasKey(x => new { x.SuggestionId, x.UserId });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<FeedbackMessage>(e =>
        {
            e.Property(x => x.Body).HasMaxLength(4000).IsRequired();
            e.Property(x => x.PlayerRef).HasMaxLength(100);
            e.Property(x => x.AppVersion).HasMaxLength(32);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.HasOne(x => x.Game).WithMany().HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
        });

        b.Entity<ApiUsage>(e =>
        {
            e.HasKey(x => new { x.Date, x.Provider });
            e.Property(x => x.Provider).HasMaxLength(32);
        });

        b.Entity<SyncState>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(64);
        });

        b.Entity<ApiKey>(e =>
        {
            e.Property(x => x.KeyPrefix).HasMaxLength(16).IsRequired();
            e.Property(x => x.KeyHash).HasMaxLength(128).IsRequired();
            e.Ignore(x => x.IsActive);
            e.HasIndex(x => x.KeyHash).IsUnique();
            e.HasOne(x => x.Game).WithMany().HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
