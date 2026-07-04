using Microsoft.EntityFrameworkCore;

namespace SpendGovernor.Infrastructure.Persistence;

public sealed class SpendGovernorDbContext : DbContext
{
    public SpendGovernorDbContext(DbContextOptions<SpendGovernorDbContext> options)
        : base(options)
    {
    }

    public DbSet<Repository> Repositories => Set<Repository>();

    public DbSet<PullRequestScan> PullRequestScans => Set<PullRequestScan>();

    public DbSet<CostBreakdownItem> CostBreakdownItems => Set<CostBreakdownItem>();

    public DbSet<DetectedResource> DetectedResources => Set<DetectedResource>();

    public DbSet<ScanAssumption> ScanAssumptions => Set<ScanAssumption>();

    public DbSet<PolicyEvaluation> PolicyEvaluations => Set<PolicyEvaluation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureRepository(modelBuilder);
        ConfigurePullRequestScan(modelBuilder);
        ConfigureCostBreakdownItem(modelBuilder);
        ConfigureDetectedResource(modelBuilder, Database.IsSqlServer());
        ConfigureScanAssumption(modelBuilder);
        ConfigurePolicyEvaluation(modelBuilder);
    }

    private static void ConfigureRepository(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Repository>();
        entity.ToTable("Repositories");
        entity.HasKey(repository => repository.Id);
        entity.Property(repository => repository.Provider).HasMaxLength(50).IsRequired();
        entity.Property(repository => repository.Owner).HasMaxLength(200).IsRequired();
        entity.Property(repository => repository.Name).HasMaxLength(200).IsRequired();
        entity.Property(repository => repository.FullName).HasMaxLength(400).IsRequired();
        entity.Property(repository => repository.DefaultBranch).HasMaxLength(200).IsRequired();
        entity.Property(repository => repository.ExternalRepositoryId).HasMaxLength(200);
        entity.Property(repository => repository.InstallationId).HasMaxLength(200);
        entity.HasIndex(repository => repository.FullName);
        entity.HasIndex(repository => new { repository.Provider, repository.FullName }).IsUnique();
        entity.HasMany(repository => repository.PullRequestScans)
            .WithOne(scan => scan.Repository)
            .HasForeignKey(scan => scan.RepositoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigurePullRequestScan(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PullRequestScan>();
        entity.ToTable("PullRequestScans");
        entity.HasKey(scan => scan.Id);
        entity.Property(scan => scan.SourceBranch).HasMaxLength(200).IsRequired();
        entity.Property(scan => scan.TargetBranch).HasMaxLength(200).IsRequired();
        entity.Property(scan => scan.Environment).HasMaxLength(100);
        entity.Property(scan => scan.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
        entity.Property(scan => scan.Decision).HasConversion<string>().HasMaxLength(50).IsRequired();
        entity.Property(scan => scan.EstimatedMonthlyDelta).HasPrecision(18, 2);
        entity.Property(scan => scan.Currency).HasMaxLength(10).IsRequired();
        entity.Property(scan => scan.ConfidenceLevel).HasConversion<string>().HasMaxLength(50).IsRequired();
        entity.Property(scan => scan.FailureReason).HasMaxLength(2000);
        entity.Property(scan => scan.DashboardReportUrl).HasMaxLength(1000);
        entity.Property(scan => scan.GitHubCommentId).HasMaxLength(200);
        entity.Property(scan => scan.GitHubPullRequestUrl).HasMaxLength(1000);
        entity.HasIndex(scan => new { scan.RepositoryId, scan.PullRequestNumber });
        entity.HasIndex(scan => scan.Status);
        entity.HasIndex(scan => scan.Decision);
        entity.HasIndex(scan => scan.CreatedAt);
        entity.HasIndex(scan => scan.GitHubCommentId);
        entity.HasMany(scan => scan.CostBreakdownItems)
            .WithOne(item => item.PullRequestScan)
            .HasForeignKey(item => item.PullRequestScanId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(scan => scan.DetectedResources)
            .WithOne(resource => resource.PullRequestScan)
            .HasForeignKey(resource => resource.PullRequestScanId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(scan => scan.ScanAssumptions)
            .WithOne(assumption => assumption.PullRequestScan)
            .HasForeignKey(assumption => assumption.PullRequestScanId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(scan => scan.PolicyEvaluations)
            .WithOne(evaluation => evaluation.PullRequestScan)
            .HasForeignKey(evaluation => evaluation.PullRequestScanId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureCostBreakdownItem(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CostBreakdownItem>();
        entity.ToTable("CostBreakdownItems");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.ResourceName).HasMaxLength(400).IsRequired();
        entity.Property(item => item.ResourceType).HasMaxLength(400).IsRequired();
        entity.Property(item => item.ChangeType).HasConversion<string>().HasMaxLength(50).IsRequired();
        entity.Property(item => item.EstimatedMonthlyCost).HasPrecision(18, 2);
        entity.Property(item => item.Currency).HasMaxLength(10).IsRequired();
        entity.Property(item => item.Reason).HasMaxLength(1000);
    }

    private static void ConfigureDetectedResource(ModelBuilder modelBuilder, bool isSqlServer)
    {
        var entity = modelBuilder.Entity<DetectedResource>();
        entity.ToTable("DetectedResources");
        entity.HasKey(resource => resource.Id);
        entity.Property(resource => resource.SourceFile).HasMaxLength(1000).IsRequired();
        entity.Property(resource => resource.Provider).HasMaxLength(100).IsRequired();
        entity.Property(resource => resource.ResourceType).HasMaxLength(400).IsRequired();
        entity.Property(resource => resource.ResourceName).HasMaxLength(400).IsRequired();
        entity.Property(resource => resource.Sku).HasMaxLength(200);
        entity.Property(resource => resource.Region).HasMaxLength(100);
        if (isSqlServer)
        {
            entity.Property(resource => resource.RawJson).HasColumnType("nvarchar(max)");
        }
    }

    private static void ConfigureScanAssumption(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ScanAssumption>();
        entity.ToTable("ScanAssumptions");
        entity.HasKey(assumption => assumption.Id);
        entity.Property(assumption => assumption.Name).HasMaxLength(200).IsRequired();
        entity.Property(assumption => assumption.Value).HasMaxLength(2000).IsRequired();
    }

    private static void ConfigurePolicyEvaluation(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PolicyEvaluation>();
        entity.ToTable("PolicyEvaluations");
        entity.HasKey(evaluation => evaluation.Id);
        entity.Property(evaluation => evaluation.RuleName).HasMaxLength(200).IsRequired();
        entity.Property(evaluation => evaluation.Result).HasConversion<string>().HasMaxLength(50).IsRequired();
        entity.Property(evaluation => evaluation.Message).HasMaxLength(2000).IsRequired();
    }
}
