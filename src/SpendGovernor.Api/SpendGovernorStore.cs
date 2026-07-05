using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SpendGovernor.Core;
using SpendGovernor.Infrastructure.Persistence;
using DbEnvironmentBudget = SpendGovernor.Infrastructure.Persistence.EnvironmentBudget;

public sealed class SpendGovernorStore
{
    private const string DemoEmail = "demo@spendgov.local";
    private static readonly object Gate = new();
    private static readonly Dictionary<Guid, AnalysisResult> Analyses = [];
    private static readonly Dictionary<Guid, AnalysisRequest> AnalysisRequests = [];
    private static readonly List<Approval> Approvals = [];
    private static readonly List<AuditEvent> AuditEvents = [];
    private static readonly GitHubPrCommentTracker GithubComments = new();

    private readonly SpendGovernorDbContext dbContext;

    public SpendGovernorStore(SpendGovernorDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    public AuthAttemptResult RegisterUser(string email, string password, string? displayName)
    {
        email = NormalizeEmail(email);
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return new AuthAttemptResult(null, "Email and password are required.");
        }

        if (dbContext.ApplicationUsers.Any(user => user.Email == email))
        {
            return new AuthAttemptResult(null, "A user with that email already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var user = new ApplicationUser
        {
            Email = email,
            UserName = email,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? DisplayNameFromEmail(email) : displayName.Trim(),
            PasswordHash = HashPassword(password),
            CreatedAt = now,
            UpdatedAt = now,
            LastLoginAt = now
        };
        dbContext.ApplicationUsers.Add(user);
        dbContext.SaveChanges();
        EnsureOnboardingWorkspace(user);
        return new AuthAttemptResult(MapUser(user), null);
    }

    public AuthAttemptResult LoginUser(string email, string password)
    {
        email = NormalizeEmail(email);
        var user = dbContext.ApplicationUsers.FirstOrDefault(item => item.Email == email);
        if (user is null || string.IsNullOrWhiteSpace(user.PasswordHash) || !VerifyPassword(password, user.PasswordHash))
        {
            return new AuthAttemptResult(null, "Invalid email or password.");
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        dbContext.SaveChanges();
        EnsureOnboardingWorkspace(user);
        return new AuthAttemptResult(MapUser(user), null);
    }

    public User? GetUserById(Guid userId)
    {
        var entity = dbContext.ApplicationUsers.FirstOrDefault(user => user.Id == userId);
        if (entity is null)
        {
            return null;
        }

        EnsureOnboardingWorkspace(entity);
        return MapUser(entity);
    }

    public User GetOrCreateUser(string email)
    {
        email = NormalizeEmail(email);
        var user = dbContext.ApplicationUsers.FirstOrDefault(item => item.Email == email);
        if (user is null)
        {
            var now = DateTimeOffset.UtcNow;
            user = new ApplicationUser
            {
                Email = email,
                UserName = email,
                DisplayName = DisplayNameFromEmail(email),
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.ApplicationUsers.Add(user);
            dbContext.SaveChanges();
        }

        EnsureOnboardingWorkspace(user);
        return MapUser(user);
    }

    public IReadOnlyList<Workspace> GetWorkspaces(Guid userId)
    {
        return dbContext.WorkspaceMembers
            .Where(member => member.UserId == userId)
            .Include(member => member.Workspace)
            .AsNoTracking()
            .Select(member => member.Workspace!)
            .ToArray()
            .OrderBy(workspace => workspace.CreatedAt)
            .Select(MapWorkspace)
            .ToArray();
    }

    public Workspace? GetWorkspace(Guid workspaceId)
    {
        var workspace = dbContext.Workspaces.AsNoTracking().FirstOrDefault(item => item.Id == workspaceId);
        return workspace is null ? null : MapWorkspace(workspace);
    }

    public Workspace CreateWorkspace(User owner, string name)
    {
        var workspace = new WorkspaceEntity
        {
            Name = string.IsNullOrWhiteSpace(name) ? "New Workspace" : name.Trim(),
            Slug = UniqueWorkspaceSlug(name),
            CreatedByUserId = owner.Id
        };
        dbContext.Workspaces.Add(workspace);
        dbContext.WorkspaceMembers.Add(new WorkspaceMemberEntity
        {
            WorkspaceId = workspace.Id,
            UserId = owner.Id,
            Role = WorkspaceRole.Owner
        });
        dbContext.SaveChanges();
        AddAudit(workspace.Id, null, null, "Workspace created", $"Workspace {workspace.Name} created.");
        return MapWorkspace(workspace);
    }

    public bool CanAccessWorkspace(Guid userId, Guid workspaceId)
    {
        return dbContext.WorkspaceMembers.Any(member => member.UserId == userId && member.WorkspaceId == workspaceId);
    }

    public bool CanEditWorkspace(Guid userId, Guid workspaceId)
    {
        return dbContext.WorkspaceMembers.Any(member =>
            member.UserId == userId
            && member.WorkspaceId == workspaceId
            && member.Role == WorkspaceRole.Owner);
    }

    public IReadOnlyList<Project> GetProjects(Guid workspaceId)
    {
        return dbContext.Projects
            .Where(project => project.WorkspaceId == workspaceId)
            .Include(project => project.Repositories)
            .Include(project => project.EnvironmentBudgets)
            .AsNoTracking()
            .ToArray()
            .OrderBy(project => project.CreatedAt)
            .Select(MapProject)
            .ToArray();
    }

    public Project? GetProject(Guid projectId)
    {
        var project = LoadProject(projectId);
        return project is null ? null : MapProject(project);
    }

    public Project? GetProjectForUser(Guid projectId, Guid userId)
    {
        var project = LoadProject(projectId);
        return project is not null && CanAccessWorkspace(userId, project.WorkspaceId) ? MapProject(project) : null;
    }

    public Project? GetProjectForRepositoryForUser(string owner, string name, Guid userId)
    {
        var fullName = $"{owner}/{name}";
        var repository = dbContext.Repositories
            .Include(item => item.Project!)
                .ThenInclude(project => project.Repositories)
            .Include(item => item.Project!)
                .ThenInclude(project => project.EnvironmentBudgets)
            .AsNoTracking()
            .FirstOrDefault(item => item.Provider == "github" && item.FullName == fullName);
        return repository?.Project is not null && CanAccessWorkspace(userId, repository.Project.WorkspaceId)
            ? MapProject(repository.Project)
            : null;
    }

    public Project CreateProject(CreateProjectRequest request)
    {
        var project = new ProjectEntity
        {
            WorkspaceId = request.WorkspaceId,
            Name = string.IsNullOrWhiteSpace(request.Name) ? request.RepositoryName : request.Name.Trim(),
            Slug = UniqueProjectSlug(request.WorkspaceId, request.Name),
            Provider = "azure",
            DefaultRegion = string.IsNullOrWhiteSpace(request.DefaultRegion) ? "westeurope" : request.DefaultRegion.Trim(),
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? "EUR" : request.Currency.Trim().ToUpperInvariant(),
            HoursPerMonth = request.HoursPerMonth ?? 730,
            PolicyYaml = PolicyConfig.DefaultYaml
        };
        dbContext.Projects.Add(project);
        dbContext.SaveChanges();
        AddDefaultBudgets(project);
        AddAudit(project.WorkspaceId, project.Id, null, "Project created", $"Project {project.Name} linked to {request.RepositoryOwner}/{request.RepositoryName}.");

        return new Project
        {
            Id = project.Id,
            WorkspaceId = project.WorkspaceId,
            Name = project.Name,
            Provider = project.Provider,
            RepositoryOwner = request.RepositoryOwner,
            RepositoryName = request.RepositoryName,
            DefaultRegion = project.DefaultRegion,
            Currency = project.Currency,
            HoursPerMonth = project.HoursPerMonth,
            PolicyYaml = EffectivePolicyYaml(project)
        };
    }

    public Project EnsureDemoProject(User owner)
    {
        var project = dbContext.Repositories
            .Where(repository => repository.Provider == "github" && repository.FullName == "acme/spend-governor-demo")
            .Include(repository => repository.Project!)
                .ThenInclude(item => item.Repositories)
            .Include(repository => repository.Project!)
                .ThenInclude(item => item.EnvironmentBudgets)
            .Select(repository => repository.Project)
            .FirstOrDefault();
        if (project is not null)
        {
            EnsureWorkspaceMembership(project.WorkspaceId, owner.Id, WorkspaceRole.Owner);
            return MapProject(project);
        }

        var workspace = dbContext.WorkspaceMembers
            .Where(member => member.UserId == owner.Id)
            .Include(member => member.Workspace)
            .AsEnumerable()
            .OrderBy(member => member.Workspace!.CreatedAt)
            .Select(member => member.Workspace)
            .FirstOrDefault()
            ?? CreateWorkspaceEntity(owner.Id, "Acme Cloud Team");

        project = new ProjectEntity
        {
            WorkspaceId = workspace.Id,
            Name = "Cloud & AI Spend Governor demo",
            Slug = UniqueProjectSlug(workspace.Id, "Cloud & AI Spend Governor demo"),
            Provider = "azure",
            DefaultRegion = "westeurope",
            Currency = "EUR",
            HoursPerMonth = 730,
            PolicyYaml = PolicyConfig.DefaultYaml
        };
        dbContext.Projects.Add(project);
        dbContext.SaveChanges();
        AddDefaultBudgets(project);
        AddAudit(project.WorkspaceId, project.Id, null, "Project created", "Demo project linked to acme/spend-governor-demo.");
        return new Project
        {
            Id = project.Id,
            WorkspaceId = project.WorkspaceId,
            Name = project.Name,
            Provider = project.Provider,
            RepositoryOwner = "acme",
            RepositoryName = "spend-governor-demo",
            DefaultRegion = project.DefaultRegion,
            Currency = project.Currency,
            HoursPerMonth = project.HoursPerMonth,
            PolicyYaml = EffectivePolicyYaml(project)
        };
    }

    public void UpdateProject(Guid projectId, PatchProjectSettingsRequest request)
    {
        var project = dbContext.Projects
            .Include(item => item.EnvironmentBudgets)
            .First(item => item.Id == projectId);
        if (!string.IsNullOrWhiteSpace(request.DefaultRegion))
        {
            project.DefaultRegion = request.DefaultRegion.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Currency))
        {
            project.Currency = request.Currency.Trim().ToUpperInvariant();
        }

        if (request.HoursPerMonth is not null)
        {
            project.HoursPerMonth = request.HoursPerMonth.Value;
        }

        if (!string.IsNullOrWhiteSpace(request.PolicyYaml))
        {
            project.PolicyYaml = request.PolicyYaml;
        }

        project.UpdatedAt = DateTimeOffset.UtcNow;
        dbContext.SaveChanges();
        AddAudit(project.WorkspaceId, project.Id, null, "Project settings changed", "Project default settings were updated.");
    }

    public PolicyResponse UpdateProjectPolicy(Guid projectId, string yaml)
    {
        var project = dbContext.Projects.First(item => item.Id == projectId);
        project.PolicyYaml = yaml;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        dbContext.SaveChanges();
        var mapped = GetProject(projectId)!;
        AddAudit(project.WorkspaceId, project.Id, null, "Config changed", ".spendgov.yml policy settings were updated from the dashboard.");
        return new PolicyResponse(mapped.PolicyYaml, SpendGovConfigParser.Parse(mapped.PolicyYaml, ProjectSettings.FromProject(mapped)));
    }

    public void UpdateGitHubInstallation(Guid projectId, string installationId)
    {
        var repositories = dbContext.Repositories.Where(repository => repository.ProjectId == projectId).ToArray();
        foreach (var repository in repositories)
        {
            repository.InstallationId = installationId;
            repository.UpdatedAt = DateTimeOffset.UtcNow;
        }

        dbContext.SaveChanges();
    }

    public Project? FindProjectByRepository(string owner, string name, string? installationId)
    {
        var fullName = $"{owner}/{name}";
        var query = dbContext.Repositories
            .Where(repository => repository.Provider == "github" && repository.FullName == fullName);
        if (!string.IsNullOrWhiteSpace(installationId))
        {
            query = query.Where(repository => repository.InstallationId == null || repository.InstallationId == "" || repository.InstallationId == installationId);
        }

        var repository = query
            .Include(item => item.Project!)
                .ThenInclude(project => project.Repositories)
            .Include(item => item.Project!)
                .ThenInclude(project => project.EnvironmentBudgets)
            .AsNoTracking()
            .FirstOrDefault();
        return repository?.Project is null ? null : MapProject(repository.Project);
    }

    public IReadOnlyList<EnvironmentBudgetItem> GetBudgets(Guid projectId)
    {
        return dbContext.EnvironmentBudgets
            .Where(budget => budget.ProjectId == projectId)
            .AsNoTracking()
            .OrderBy(budget => budget.Environment)
            .Select(MapBudget)
            .ToArray();
    }

    public IReadOnlyList<RepositoryListItem> GetRepositories(Guid projectId)
    {
        return dbContext.Repositories
            .Where(repository => repository.ProjectId == projectId)
            .Include(repository => repository.PullRequestScans)
            .AsNoTracking()
            .ToArray()
            .OrderBy(repository => repository.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(repository =>
            {
                var lastScan = repository.PullRequestScans
                    .OrderByDescending(scan => scan.CreatedAt)
                    .FirstOrDefault();
                return new RepositoryListItem(
                    repository.Id,
                    repository.ProjectId,
                    repository.Provider,
                    repository.Owner,
                    repository.Name,
                    repository.FullName,
                    repository.DefaultBranch,
                    repository.ExternalRepositoryId,
                    repository.InstallationId,
                    lastScan?.CreatedAt);
            })
            .ToArray();
    }

    public EnvironmentBudgetItem UpsertBudget(Guid projectId, EnvironmentBudgetUpdateRequest request)
    {
        var environment = string.IsNullOrWhiteSpace(request.Environment) ? "dev" : request.Environment.Trim().ToLowerInvariant();
        var project = dbContext.Projects
            .Include(item => item.EnvironmentBudgets)
            .First(item => item.Id == projectId);
        var budget = project.EnvironmentBudgets.FirstOrDefault(item => item.Environment == environment);
        if (budget is null)
        {
            budget = new DbEnvironmentBudget
            {
                ProjectId = projectId,
                Environment = environment,
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.EnvironmentBudgets.Add(budget);
        }

        budget.MaxMonthlyCost = request.MaxMonthlyCost;
        budget.MaxMonthlyDelta = request.MaxMonthlyDelta;
        budget.RequireApprovalAbove = request.RequireApprovalAbove;
        budget.BlockOnBudgetExceeded = request.BlockOnBudgetExceeded ?? budget.BlockOnBudgetExceeded;
        budget.Currency = string.IsNullOrWhiteSpace(request.Currency) ? project.Currency : request.Currency.Trim().ToUpperInvariant();
        budget.UpdatedAt = DateTimeOffset.UtcNow;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        dbContext.SaveChanges();
        AddAudit(project.WorkspaceId, project.Id, null, "Budget changed", $"{environment} budget settings were updated.");
        return MapBudget(budget);
    }

    public GitHubPrCommentState SaveAnalysis(Project project, AnalysisResult result, AnalysisRequest request, string? existingGitHubCommentId = null)
    {
        lock (Gate)
        {
            RecordAnalysisCore(project, result, request);
            var commentState = GithubComments.Upsert(result.Analysis, result.CommentMarkdown, result.CheckConclusion, existingGitHubCommentId);
            AuditEvents.Add(new AuditEvent
            {
                WorkspaceId = project.WorkspaceId,
                ProjectId = project.Id,
                AnalysisId = result.Analysis.Id,
                EventType = commentState.WasCreatedOnLastWrite ? "GitHub PR comment created" : "GitHub PR comment updated",
                Message = $"Simulated Spend Governor PR comment #{commentState.CommentId} for PR #{result.Analysis.PullRequestNumber}."
            });
            AuditEvents.Add(new AuditEvent
            {
                WorkspaceId = project.WorkspaceId,
                ProjectId = project.Id,
                AnalysisId = result.Analysis.Id,
                EventType = "GitHub Check Run simulated",
                Message = $"Spend Governor check conclusion: {result.CheckConclusion}."
            });
            return commentState;
        }
    }

    public void RecordAnalysis(Project project, AnalysisResult result, AnalysisRequest request)
    {
        lock (Gate)
        {
            RecordAnalysisCore(project, result, request);
        }
    }

    public GitHubPrCommentState? GetGitHubComment(string owner, string name, int pullRequestNumber)
    {
        return GithubComments.Get(owner, name, pullRequestNumber);
    }

    public AnalysisRequest? GetRequest(Guid analysisId)
    {
        lock (Gate)
        {
            return AnalysisRequests.GetValueOrDefault(analysisId);
        }
    }

    public AnalysisResult? GetAnalysisForUser(Guid analysisId, Guid userId)
    {
        lock (Gate)
        {
            if (!Analyses.TryGetValue(analysisId, out var result))
            {
                return null;
            }

            var project = GetProject(result.Analysis.ProjectId);
            return project is not null && CanAccessWorkspace(userId, project.WorkspaceId) ? result : null;
        }
    }

    public IReadOnlyList<AnalysisResult> GetAnalyses(Guid projectId)
    {
        lock (Gate)
        {
            return Analyses.Values
                .Where(result => result.Analysis.ProjectId == projectId)
                .OrderByDescending(result => result.Analysis.CreatedAt)
                .ToArray();
        }
    }

    public ProjectMetrics GetProjectMetrics(Guid projectId)
    {
        lock (Gate)
        {
            var projectAnalyses = GetAnalyses(projectId);
            var totalDelta = projectAnalyses.Sum(result => result.Analysis.MonthlyDelta ?? 0);
            var warnedOrBlocked = projectAnalyses.Count(result => result.Analysis.PolicyStatus is PolicyAction.Warn or PolicyAction.ApprovalRequired or PolicyAction.Block);
            return new ProjectMetrics(
                projectAnalyses.Count,
                decimal.Round(totalDelta, 2),
                warnedOrBlocked,
                projectAnalyses.Take(5).Select(AnalysisListItem.FromResult).ToArray());
        }
    }

    public Approval Approve(AnalysisResult result, User user, string reason)
    {
        lock (Gate)
        {
            var approval = new Approval
            {
                AnalysisId = result.Analysis.Id,
                ApprovedByUserId = user.Id,
                CommitSha = result.Analysis.CommitSha,
                Reason = reason
            };
            Approvals.Add(approval);
            result.Analysis.PolicyStatus = PolicyAction.Pass;
            result.CheckConclusion = "success";
            var project = GetProject(result.Analysis.ProjectId);
            if (project is not null)
            {
                AddAudit(project.WorkspaceId, project.Id, result.Analysis.Id, "Approval granted", $"Approval granted by {user.Email}: {reason}");
            }

            return approval;
        }
    }

    public IReadOnlyList<object> GetApprovals(Guid projectId)
    {
        lock (Gate)
        {
            var projectAnalysisIds = Analyses.Values
                .Where(result => result.Analysis.ProjectId == projectId)
                .Select(result => result.Analysis.Id)
                .ToHashSet();
            return Approvals
                .Where(approval => projectAnalysisIds.Contains(approval.AnalysisId))
                .OrderByDescending(approval => approval.CreatedAt)
                .Select(approval => new
                {
                    approval.Id,
                    approval.AnalysisId,
                    approval.ApprovedByUserId,
                    approval.CommitSha,
                    approval.Reason,
                    approval.CreatedAt
                })
                .Cast<object>()
                .ToArray();
        }
    }

    public IReadOnlyList<AuditEvent> GetAudit(Guid projectId)
    {
        lock (Gate)
        {
            return AuditEvents
                .Where(audit => audit.ProjectId == projectId)
                .OrderByDescending(audit => audit.CreatedAt)
                .ToArray();
        }
    }

    public void AddAudit(Guid workspaceId, Guid? projectId, Guid? analysisId, string eventType, string message)
    {
        lock (Gate)
        {
            AuditEvents.Add(new AuditEvent
            {
                WorkspaceId = workspaceId,
                ProjectId = projectId,
                AnalysisId = analysisId,
                EventType = eventType,
                Message = message
            });
        }
    }

    private void RecordAnalysisCore(Project project, AnalysisResult result, AnalysisRequest request)
    {
        foreach (var audit in result.AuditEvents)
        {
            audit.WorkspaceId = project.WorkspaceId;
            audit.ProjectId = project.Id;
        }

        Analyses[result.Analysis.Id] = result;
        AnalysisRequests[result.Analysis.Id] = request;
        AuditEvents.AddRange(result.AuditEvents);
    }

    private void EnsureOnboardingWorkspace(ApplicationUser user)
    {
        if (dbContext.WorkspaceMembers.Any(member => member.UserId == user.Id))
        {
            if (user.Email.Equals(DemoEmail, StringComparison.OrdinalIgnoreCase))
            {
                var mapped = MapUser(user);
                EnsureDemoProject(mapped);
            }

            return;
        }

        var workspaceName = user.Email.Equals(DemoEmail, StringComparison.OrdinalIgnoreCase)
            ? "Acme Cloud Team"
            : $"{user.DisplayName}'s Workspace";
        var workspace = CreateWorkspaceEntity(user.Id, workspaceName);
        if (user.Email.Equals(DemoEmail, StringComparison.OrdinalIgnoreCase))
        {
            EnsureDemoProject(MapUser(user));
        }
        else
        {
            AddAudit(workspace.Id, null, null, "Workspace created", $"Workspace {workspace.Name} created.");
        }
    }

    private WorkspaceEntity CreateWorkspaceEntity(Guid ownerId, string name)
    {
        var workspace = new WorkspaceEntity
        {
            Name = name,
            Slug = UniqueWorkspaceSlug(name),
            CreatedByUserId = ownerId
        };
        dbContext.Workspaces.Add(workspace);
        dbContext.WorkspaceMembers.Add(new WorkspaceMemberEntity
        {
            WorkspaceId = workspace.Id,
            UserId = ownerId,
            Role = WorkspaceRole.Owner
        });
        dbContext.SaveChanges();
        return workspace;
    }

    private void EnsureWorkspaceMembership(Guid workspaceId, Guid userId, WorkspaceRole role)
    {
        var existing = dbContext.WorkspaceMembers.FirstOrDefault(member => member.WorkspaceId == workspaceId && member.UserId == userId);
        if (existing is not null)
        {
            if (role == WorkspaceRole.Owner && existing.Role != WorkspaceRole.Owner)
            {
                existing.Role = WorkspaceRole.Owner;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                dbContext.SaveChanges();
            }

            return;
        }

        dbContext.WorkspaceMembers.Add(new WorkspaceMemberEntity
        {
            WorkspaceId = workspaceId,
            UserId = userId,
            Role = role
        });
        dbContext.SaveChanges();
    }

    private ProjectEntity? LoadProject(Guid projectId)
    {
        return dbContext.Projects
            .Include(project => project.Repositories)
            .Include(project => project.EnvironmentBudgets)
            .AsNoTracking()
            .FirstOrDefault(project => project.Id == projectId);
    }

    private void AddDefaultBudgets(ProjectEntity project)
    {
        if (dbContext.EnvironmentBudgets.Any(budget => budget.ProjectId == project.Id))
        {
            return;
        }

        dbContext.EnvironmentBudgets.AddRange(
            Budget(project.Id, "dev", 100, 100, "EUR", null, false),
            Budget(project.Id, "staging", 250, 250, "EUR", 250, false),
            Budget(project.Id, "production", 1000, 250, "EUR", null, true));
        dbContext.SaveChanges();
    }

    private static DbEnvironmentBudget Budget(Guid projectId, string environment, decimal monthlyCost, decimal monthlyDelta, string currency, decimal? approval, bool block)
    {
        return new DbEnvironmentBudget
        {
            ProjectId = projectId,
            Environment = environment,
            MaxMonthlyCost = monthlyCost,
            MaxMonthlyDelta = monthlyDelta,
            Currency = currency,
            RequireApprovalAbove = approval,
            BlockOnBudgetExceeded = block
        };
    }

    private static Workspace MapWorkspace(WorkspaceEntity workspace)
    {
        return new Workspace(workspace.Id, workspace.Name, workspace.CreatedAt);
    }

    private static User MapUser(ApplicationUser user)
    {
        return new User(user.Id, user.Email, user.DisplayName, user.CreatedAt);
    }

    private static Project MapProject(ProjectEntity project)
    {
        var repository = project.Repositories
            .OrderBy(repository => repository.CreatedAt)
            .FirstOrDefault();
        return new Project
        {
            Id = project.Id,
            WorkspaceId = project.WorkspaceId,
            Name = project.Name,
            Provider = project.Provider,
            Currency = project.Currency,
            DefaultRegion = project.DefaultRegion,
            HoursPerMonth = project.HoursPerMonth,
            RepositoryProvider = repository?.Provider ?? "github",
            RepositoryOwner = repository?.Owner ?? "",
            RepositoryName = repository?.Name ?? "",
            GitHubInstallationId = repository?.InstallationId,
            PolicyYaml = EffectivePolicyYaml(project),
            CreatedAt = project.CreatedAt
        };
    }

    private static EnvironmentBudgetItem MapBudget(DbEnvironmentBudget budget)
    {
        return new EnvironmentBudgetItem(
            budget.Id,
            budget.ProjectId,
            budget.Environment,
            budget.MaxMonthlyCost,
            budget.MaxMonthlyDelta,
            budget.Currency,
            budget.RequireApprovalAbove,
            budget.BlockOnBudgetExceeded,
            budget.CreatedAt,
            budget.UpdatedAt);
    }

    private static string EffectivePolicyYaml(ProjectEntity project)
    {
        if (project.EnvironmentBudgets.Count == 0)
        {
            return string.IsNullOrWhiteSpace(project.PolicyYaml) ? PolicyConfig.DefaultYaml : project.PolicyYaml;
        }

        var builder = new StringBuilder();
        builder.AppendLine("# BudgetSource: DatabaseProjectEnvironmentBudget");
        builder.AppendLine("version: 1");
        builder.AppendLine($"currency: {project.Currency}");
        builder.AppendLine($"defaultRegion: {project.DefaultRegion}");
        builder.AppendLine($"hoursPerMonth: {project.HoursPerMonth.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine("onInternalError: fail_open");
        builder.AppendLine();
        builder.AppendLine("pullRequests:");
        builder.AppendLine("  comment: true");
        builder.AppendLine("  checkRun: true");
        builder.AppendLine();
        builder.AppendLine("rules:");
        var deltaBudgets = project.EnvironmentBudgets
            .Where(budget => budget.MaxMonthlyDelta is > 0)
            .OrderBy(budget => budget.Environment)
            .ToArray();
        foreach (var budget in deltaBudgets)
        {
            builder.AppendLine($"  - id: {YamlToken(budget.Environment)}-monthly-delta");
            builder.AppendLine($"    description: Guard {budget.Environment} PR monthly delta");
            builder.AppendLine("    type: monthly_delta");
            builder.AppendLine($"    threshold: {budget.MaxMonthlyDelta!.Value.ToString("0.##", CultureInfo.InvariantCulture)}");
            builder.AppendLine($"    action: {SpendGovConfigParser.ToConfigString(BudgetAction(budget))}");
        }

        builder.AppendLine("  - id: max-unknown-resources");
        builder.AppendLine("    description: Warn when too many resources cannot be estimated");
        builder.AppendLine("    type: unknown_resource_count");
        builder.AppendLine("    threshold: 3");
        builder.AppendLine("    action: warn");
        builder.AppendLine();
        builder.AppendLine("environments:");
        foreach (var budget in project.EnvironmentBudgets.OrderBy(budget => budget.Environment))
        {
            builder.AppendLine($"  {YamlToken(budget.Environment)}:");
            builder.AppendLine($"    monthlyBudget: {(budget.MaxMonthlyCost ?? budget.MaxMonthlyDelta ?? 1).ToString("0.##", CultureInfo.InvariantCulture)}");
            builder.AppendLine($"    action: {SpendGovConfigParser.ToConfigString(BudgetAction(budget))}");
        }

        builder.AppendLine();
        builder.AppendLine("ai:");
        builder.AppendLine("  enabled: true");
        builder.AppendLine("  monthlyBudget: 300");
        builder.AppendLine("  maxCostPerWorkflowMonthly: 100");
        builder.AppendLine("  action: warn");
        return builder.ToString();
    }

    private static PolicyAction BudgetAction(DbEnvironmentBudget budget)
    {
        if (budget.BlockOnBudgetExceeded)
        {
            return PolicyAction.Block;
        }

        return budget.RequireApprovalAbove is > 0 ? PolicyAction.ApprovalRequired : PolicyAction.Warn;
    }

    private string UniqueWorkspaceSlug(string name)
    {
        return UniqueSlug(Slugify(name), candidate => dbContext.Workspaces.Any(workspace => workspace.Slug == candidate));
    }

    private string UniqueProjectSlug(Guid workspaceId, string name)
    {
        return UniqueSlug(Slugify(name), candidate => dbContext.Projects.Any(project => project.WorkspaceId == workspaceId && project.Slug == candidate));
    }

    private static string UniqueSlug(string baseSlug, Func<string, bool> exists)
    {
        if (string.IsNullOrWhiteSpace(baseSlug))
        {
            baseSlug = "item";
        }

        var candidate = baseSlug;
        var counter = 2;
        while (exists(candidate))
        {
            candidate = $"{baseSlug}-{counter.ToString(CultureInfo.InvariantCulture)}";
            counter++;
        }

        return candidate;
    }

    private static string Slugify(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }

    private static string YamlToken(string value)
    {
        return Slugify(value).Replace('-', '_');
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    private static string DisplayNameFromEmail(string email)
    {
        var prefix = email.Split('@', 2)[0];
        return string.IsNullOrWhiteSpace(prefix) ? email : prefix;
    }

    private static string HashPassword(string password)
    {
        const int iterations = 100_000;
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256:{iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split(':');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var iterations))
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

public sealed record AuthAttemptResult(User? User, string? Error);
