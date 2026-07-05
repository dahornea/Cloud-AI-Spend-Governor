using Microsoft.EntityFrameworkCore;

namespace SpendGovernor.Infrastructure.Persistence;

public sealed class SpendGovernorDbContext : DbContext
{
    public SpendGovernorDbContext(DbContextOptions<SpendGovernorDbContext> options)
        : base(options)
    {
    }

    public DbSet<Repository> Repositories => Set<Repository>();

    public DbSet<ApplicationUser> ApplicationUsers => Set<ApplicationUser>();

    public DbSet<WorkspaceEntity> Workspaces => Set<WorkspaceEntity>();

    public DbSet<WorkspaceMemberEntity> WorkspaceMembers => Set<WorkspaceMemberEntity>();

    public DbSet<ProjectEntity> Projects => Set<ProjectEntity>();

    public DbSet<EnvironmentBudget> EnvironmentBudgets => Set<EnvironmentBudget>();

    public DbSet<PullRequestScan> PullRequestScans => Set<PullRequestScan>();

    public DbSet<CostBreakdownItem> CostBreakdownItems => Set<CostBreakdownItem>();

    public DbSet<DetectedResource> DetectedResources => Set<DetectedResource>();

    public DbSet<ScanAssumption> ScanAssumptions => Set<ScanAssumption>();

    public DbSet<PolicyEvaluation> PolicyEvaluations => Set<PolicyEvaluation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureApplicationUser(modelBuilder);
        ConfigureWorkspace(modelBuilder);
        ConfigureWorkspaceMember(modelBuilder);
        ConfigureProject(modelBuilder);
        ConfigureEnvironmentBudget(modelBuilder);
        ConfigureRepository(modelBuilder);
        ConfigurePullRequestScan(modelBuilder);
        ConfigureCostBreakdownItem(modelBuilder);
        ConfigureDetectedResource(modelBuilder, Database.IsSqlServer());
        ConfigureScanAssumption(modelBuilder);
        ConfigurePolicyEvaluation(modelBuilder);
    }

    private static void ConfigureApplicationUser(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ApplicationUser>();
        entity.ToTable("ApplicationUsers");
        entity.HasKey(user => user.Id);
        entity.Property(user => user.Email).HasMaxLength(320).IsRequired();
        entity.Property(user => user.UserName).HasMaxLength(320).IsRequired();
        entity.Property(user => user.DisplayName).HasMaxLength(200).IsRequired();
        entity.Property(user => user.PasswordHash).HasMaxLength(1000);
        entity.HasIndex(user => user.Email).IsUnique();
        entity.HasIndex(user => user.UserName);
    }

    private static void ConfigureWorkspace(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<WorkspaceEntity>();
        entity.ToTable("Workspaces");
        entity.HasKey(workspace => workspace.Id);
        entity.Property(workspace => workspace.Name).HasMaxLength(200).IsRequired();
        entity.Property(workspace => workspace.Slug).HasMaxLength(220).IsRequired();
        entity.HasIndex(workspace => workspace.Slug).IsUnique();
        entity.HasIndex(workspace => workspace.CreatedByUserId);
        entity.HasOne(workspace => workspace.CreatedByUser)
            .WithMany()
            .HasForeignKey(workspace => workspace.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureWorkspaceMember(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<WorkspaceMemberEntity>();
        entity.ToTable("WorkspaceMembers");
        entity.HasKey(member => member.Id);
        entity.Property(member => member.Role).HasConversion<string>().HasMaxLength(50).IsRequired();
        entity.HasIndex(member => new { member.WorkspaceId, member.UserId }).IsUnique();
        entity.HasIndex(member => member.UserId);
        entity.HasIndex(member => member.WorkspaceId);
        entity.HasIndex(member => member.Role);
        entity.HasOne(member => member.Workspace)
            .WithMany(workspace => workspace.WorkspaceMembers)
            .HasForeignKey(member => member.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(member => member.User)
            .WithMany(user => user.WorkspaceMembers)
            .HasForeignKey(member => member.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureProject(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ProjectEntity>();
        entity.ToTable("Projects");
        entity.HasKey(project => project.Id);
        entity.Property(project => project.Name).HasMaxLength(200).IsRequired();
        entity.Property(project => project.Slug).HasMaxLength(220).IsRequired();
        entity.Property(project => project.Description).HasMaxLength(1000);
        entity.Property(project => project.Provider).HasMaxLength(50).IsRequired();
        entity.Property(project => project.Currency).HasMaxLength(10).IsRequired();
        entity.Property(project => project.DefaultRegion).HasMaxLength(100).IsRequired();
        entity.HasIndex(project => new { project.WorkspaceId, project.Slug }).IsUnique();
        entity.HasIndex(project => project.WorkspaceId);
        entity.HasOne(project => project.Workspace)
            .WithMany(workspace => workspace.Projects)
            .HasForeignKey(project => project.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureEnvironmentBudget(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<EnvironmentBudget>();
        entity.ToTable("EnvironmentBudgets");
        entity.HasKey(budget => budget.Id);
        entity.Property(budget => budget.Environment).HasMaxLength(100).IsRequired();
        entity.Property(budget => budget.MaxMonthlyCost).HasPrecision(18, 2);
        entity.Property(budget => budget.MaxMonthlyDelta).HasPrecision(18, 2);
        entity.Property(budget => budget.RequireApprovalAbove).HasPrecision(18, 2);
        entity.Property(budget => budget.Currency).HasMaxLength(10).IsRequired();
        entity.HasIndex(budget => new { budget.ProjectId, budget.Environment }).IsUnique();
        entity.HasOne(budget => budget.Project)
            .WithMany(project => project.EnvironmentBudgets)
            .HasForeignKey(budget => budget.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureRepository(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Repository>();
        entity.ToTable("Repositories");
        entity.HasKey(repository => repository.Id);
        entity.HasOne(repository => repository.Project)
            .WithMany(project => project.Repositories)
            .HasForeignKey(repository => repository.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(repository => repository.ProjectId);
        entity.Property(repository => repository.Provider).HasMaxLength(50).IsRequired();
        entity.Property(repository => repository.Owner).HasMaxLength(200).IsRequired();
        entity.Property(repository => repository.Name).HasMaxLength(200).IsRequired();
        entity.Property(repository => repository.FullName).HasMaxLength(400).IsRequired();
        entity.Property(repository => repository.DefaultBranch).HasMaxLength(200).IsRequired();
        entity.Property(repository => repository.ExternalRepositoryId).HasMaxLength(200);
        entity.Property(repository => repository.InstallationId).HasMaxLength(200);
        entity.HasIndex(repository => repository.FullName);
        entity.HasIndex(repository => repository.InstallationId);
        entity.HasIndex(repository => new { repository.Provider, repository.FullName });
        entity.HasIndex(repository => new { repository.ProjectId, repository.Provider, repository.FullName }).IsUnique();
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
        entity.Property(scan => scan.GitHubCheckRunId).HasMaxLength(200);
        entity.Property(scan => scan.GitHubReportUrl).HasMaxLength(1000);
        entity.Property(scan => scan.GitHubPullRequestUrl).HasMaxLength(1000);
        entity.Property(scan => scan.ReportPublishingStatus).HasMaxLength(50).HasDefaultValue("Pending").IsRequired();
        entity.Property(scan => scan.ReportPublishingError).HasMaxLength(2000);
        entity.HasIndex(scan => new { scan.RepositoryId, scan.PullRequestNumber });
        entity.HasIndex(scan => scan.Status);
        entity.HasIndex(scan => scan.Decision);
        entity.HasIndex(scan => scan.CreatedAt);
        entity.HasIndex(scan => scan.GitHubCommentId);
        entity.HasIndex(scan => scan.GitHubCheckRunId);
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
        entity.Property(item => item.BeforeSummary).HasMaxLength(1000);
        entity.Property(item => item.AfterSummary).HasMaxLength(1000);
        entity.Property(item => item.TerraformAddress).HasMaxLength(500);
        entity.Property(item => item.TerraformActions).HasMaxLength(200);
        entity.Property(item => item.PricingCatalogVersion).HasMaxLength(100);
        entity.Property(item => item.PricingSource).HasMaxLength(300);
        entity.Property(item => item.PricingMatchType).HasMaxLength(100);
        entity.Property(item => item.PricingFallbackReason).HasMaxLength(1000);
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
        entity.Property(resource => resource.TerraformAddress).HasMaxLength(500);
        entity.Property(resource => resource.TerraformActions).HasMaxLength(200);
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
