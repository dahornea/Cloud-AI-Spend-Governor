using SpendGovernor.Core;

public sealed class SpendGovernorStore
{
    private readonly object gate = new();
    private readonly List<User> users = [];
    private readonly List<Workspace> workspaces = [];
    private readonly List<WorkspaceMember> members = [];
    private readonly List<Project> projects = [];
    private readonly Dictionary<Guid, AnalysisResult> analyses = [];
    private readonly Dictionary<Guid, AnalysisRequest> analysisRequests = [];
    private readonly List<Approval> approvals = [];
    private readonly List<AuditEvent> auditEvents = [];
    private readonly GitHubPrCommentTracker githubComments = new();

    public SpendGovernorStore()
    {
        var now = DateTimeOffset.UtcNow;
        var demoUser = new User(Guid.NewGuid(), "demo@spendgov.local", "Demo Owner", now);
        var workspace = new Workspace(Guid.NewGuid(), "Acme Cloud Team", now);
        var project = new Project
        {
            WorkspaceId = workspace.Id,
            Name = "Cloud & AI Spend Governor demo",
            RepositoryOwner = "acme",
            RepositoryName = "spend-governor-demo",
            DefaultRegion = "westeurope",
            Currency = "EUR",
            HoursPerMonth = 730
        };

        users.Add(demoUser);
        workspaces.Add(workspace);
        members.Add(new WorkspaceMember(workspace.Id, demoUser.Id, WorkspaceRole.Owner));
        projects.Add(project);
        auditEvents.Add(new AuditEvent
        {
            WorkspaceId = workspace.Id,
            ProjectId = project.Id,
            EventType = "Project created",
            Message = "Demo project created for local spend-governor walkthrough.",
            CreatedAt = now
        });
    }

    public User GetOrCreateUser(string email)
    {
        lock (gate)
        {
            var user = users.FirstOrDefault(item => item.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
            if (user is not null)
            {
                return user;
            }

            user = new User(Guid.NewGuid(), email, email.Split('@')[0], DateTimeOffset.UtcNow);
            users.Add(user);
            return user;
        }
    }

    public IReadOnlyList<Workspace> GetWorkspaces(Guid userId)
    {
        lock (gate)
        {
            var workspaceIds = members.Where(member => member.UserId == userId).Select(member => member.WorkspaceId).ToHashSet();
            return workspaces.Where(workspace => workspaceIds.Contains(workspace.Id)).ToArray();
        }
    }

    public Workspace? GetWorkspace(Guid workspaceId)
    {
        lock (gate)
        {
            return workspaces.FirstOrDefault(workspace => workspace.Id == workspaceId);
        }
    }

    public Workspace CreateWorkspace(User owner, string name)
    {
        lock (gate)
        {
            var workspace = new Workspace(Guid.NewGuid(), name, DateTimeOffset.UtcNow);
            workspaces.Add(workspace);
            members.Add(new WorkspaceMember(workspace.Id, owner.Id, WorkspaceRole.Owner));
            auditEvents.Add(new AuditEvent
            {
                WorkspaceId = workspace.Id,
                EventType = "Workspace created",
                Message = $"Workspace {name} created."
            });
            return workspace;
        }
    }

    public bool CanAccessWorkspace(Guid userId, Guid workspaceId)
    {
        lock (gate)
        {
            return members.Any(member => member.UserId == userId && member.WorkspaceId == workspaceId);
        }
    }

    public bool CanEditWorkspace(Guid userId, Guid workspaceId)
    {
        lock (gate)
        {
            return members.Any(member =>
                member.UserId == userId
                && member.WorkspaceId == workspaceId
                && member.Role is WorkspaceRole.Owner or WorkspaceRole.Member);
        }
    }

    public IReadOnlyList<Project> GetProjects(Guid workspaceId)
    {
        lock (gate)
        {
            return projects.Where(project => project.WorkspaceId == workspaceId).ToArray();
        }
    }

    public Project? GetProject(Guid projectId)
    {
        lock (gate)
        {
            return projects.FirstOrDefault(project => project.Id == projectId);
        }
    }

    public Project? GetProjectForUser(Guid projectId, Guid userId)
    {
        lock (gate)
        {
            var project = projects.FirstOrDefault(item => item.Id == projectId);
            return project is not null && CanAccessWorkspace(userId, project.WorkspaceId) ? project : null;
        }
    }

    public Project? GetProjectForRepositoryForUser(string owner, string name, Guid userId)
    {
        lock (gate)
        {
            var project = projects.FirstOrDefault(item =>
                item.RepositoryOwner.Equals(owner, StringComparison.OrdinalIgnoreCase)
                && item.RepositoryName.Equals(name, StringComparison.OrdinalIgnoreCase));
            return project is not null && CanAccessWorkspace(userId, project.WorkspaceId) ? project : null;
        }
    }

    public Project CreateProject(CreateProjectRequest request)
    {
        lock (gate)
        {
            var project = new Project
            {
                WorkspaceId = request.WorkspaceId,
                Name = request.Name,
                RepositoryOwner = request.RepositoryOwner,
                RepositoryName = request.RepositoryName,
                DefaultRegion = request.DefaultRegion ?? "westeurope",
                Currency = request.Currency ?? "EUR",
                HoursPerMonth = request.HoursPerMonth ?? 730
            };
            projects.Add(project);
            auditEvents.Add(new AuditEvent
            {
                WorkspaceId = project.WorkspaceId,
                ProjectId = project.Id,
                EventType = "Project created",
                Message = $"Project {project.Name} linked to {project.RepositoryOwner}/{project.RepositoryName}."
            });
            return project;
        }
    }

    public Project EnsureDemoProject(User owner)
    {
        lock (gate)
        {
            var workspace = GetWorkspaces(owner.Id).FirstOrDefault()
                ?? CreateWorkspace(owner, "Acme Cloud Team");
            var project = projects.FirstOrDefault(item =>
                item.RepositoryOwner.Equals("acme", StringComparison.OrdinalIgnoreCase)
                && item.RepositoryName.Equals("spend-governor-demo", StringComparison.OrdinalIgnoreCase));
            if (project is not null)
            {
                if (!members.Any(member => member.WorkspaceId == project.WorkspaceId && member.UserId == owner.Id))
                {
                    members.Add(new WorkspaceMember(project.WorkspaceId, owner.Id, WorkspaceRole.Owner));
                }

                return project;
            }

            project = new Project
            {
                WorkspaceId = workspace.Id,
                Name = "Cloud & AI Spend Governor demo",
                RepositoryOwner = "acme",
                RepositoryName = "spend-governor-demo",
                DefaultRegion = "westeurope",
                Currency = "EUR",
                HoursPerMonth = 730
            };
            projects.Add(project);
            auditEvents.Add(new AuditEvent
            {
                WorkspaceId = project.WorkspaceId,
                ProjectId = project.Id,
                EventType = "Project created",
                Message = "Demo project linked to acme/spend-governor-demo."
            });
            return project;
        }
    }

    public void UpdateProject(Guid projectId, PatchProjectSettingsRequest request)
    {
        lock (gate)
        {
            var project = projects.First(project => project.Id == projectId);
            if (!string.IsNullOrWhiteSpace(request.DefaultRegion))
            {
                project.DefaultRegion = request.DefaultRegion;
            }

            if (!string.IsNullOrWhiteSpace(request.Currency))
            {
                project.Currency = request.Currency;
            }

            if (request.HoursPerMonth is not null)
            {
                project.HoursPerMonth = request.HoursPerMonth.Value;
            }

            if (!string.IsNullOrWhiteSpace(request.PolicyYaml))
            {
                project.PolicyYaml = request.PolicyYaml;
            }

            AddAudit(project.WorkspaceId, project.Id, null, "Project settings changed", "Project default settings were updated.");
        }
    }

    public Project? FindProjectByRepository(string owner, string name, string? installationId)
    {
        lock (gate)
        {
            return projects.FirstOrDefault(project =>
                project.RepositoryOwner.Equals(owner, StringComparison.OrdinalIgnoreCase)
                && project.RepositoryName.Equals(name, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(installationId)
                    || string.IsNullOrWhiteSpace(project.GitHubInstallationId)
                    || project.GitHubInstallationId.Equals(installationId, StringComparison.OrdinalIgnoreCase)));
        }
    }

    public GitHubPrCommentState SaveAnalysis(Project project, AnalysisResult result, AnalysisRequest request, string? existingGitHubCommentId = null)
    {
        lock (gate)
        {
            foreach (var audit in result.AuditEvents)
            {
                audit.WorkspaceId = project.WorkspaceId;
                audit.ProjectId = project.Id;
            }

            analyses[result.Analysis.Id] = result;
            analysisRequests[result.Analysis.Id] = request;
            var commentState = githubComments.Upsert(result.Analysis, result.CommentMarkdown, result.CheckConclusion, existingGitHubCommentId);
            auditEvents.AddRange(result.AuditEvents);
            auditEvents.Add(new AuditEvent
            {
                WorkspaceId = project.WorkspaceId,
                ProjectId = project.Id,
                AnalysisId = result.Analysis.Id,
                EventType = commentState.WasCreatedOnLastWrite ? "GitHub PR comment created" : "GitHub PR comment updated",
                Message = $"Simulated Spend Governor PR comment #{commentState.CommentId} for PR #{result.Analysis.PullRequestNumber}."
            });
            auditEvents.Add(new AuditEvent
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

    public GitHubPrCommentState? GetGitHubComment(string owner, string name, int pullRequestNumber)
    {
        return githubComments.Get(owner, name, pullRequestNumber);
    }

    public AnalysisRequest? GetRequest(Guid analysisId)
    {
        lock (gate)
        {
            return analysisRequests.GetValueOrDefault(analysisId);
        }
    }

    public AnalysisResult? GetAnalysisForUser(Guid analysisId, Guid userId)
    {
        lock (gate)
        {
            if (!analyses.TryGetValue(analysisId, out var result))
            {
                return null;
            }

            var project = projects.FirstOrDefault(item => item.Id == result.Analysis.ProjectId);
            return project is not null && CanAccessWorkspace(userId, project.WorkspaceId) ? result : null;
        }
    }

    public IReadOnlyList<AnalysisResult> GetAnalyses(Guid projectId)
    {
        lock (gate)
        {
            return analyses.Values
                .Where(result => result.Analysis.ProjectId == projectId)
                .OrderByDescending(result => result.Analysis.CreatedAt)
                .ToArray();
        }
    }

    public ProjectMetrics GetProjectMetrics(Guid projectId)
    {
        lock (gate)
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
        lock (gate)
        {
            var approval = new Approval
            {
                AnalysisId = result.Analysis.Id,
                ApprovedByUserId = user.Id,
                CommitSha = result.Analysis.CommitSha,
                Reason = reason
            };
            approvals.Add(approval);
            result.Analysis.PolicyStatus = PolicyAction.Pass;
            result.CheckConclusion = "success";
            var project = projects.First(project => project.Id == result.Analysis.ProjectId);
            AddAudit(project.WorkspaceId, project.Id, result.Analysis.Id, "Approval granted", $"Approval granted by {user.Email}: {reason}");
            return approval;
        }
    }

    public IReadOnlyList<object> GetApprovals(Guid projectId)
    {
        lock (gate)
        {
            var projectAnalysisIds = analyses.Values
                .Where(result => result.Analysis.ProjectId == projectId)
                .Select(result => result.Analysis.Id)
                .ToHashSet();
            return approvals
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
        lock (gate)
        {
            return auditEvents
                .Where(audit => audit.ProjectId == projectId)
                .OrderByDescending(audit => audit.CreatedAt)
                .ToArray();
        }
    }

    public void AddAudit(Guid workspaceId, Guid? projectId, Guid? analysisId, string eventType, string message)
    {
        lock (gate)
        {
            auditEvents.Add(new AuditEvent
            {
                WorkspaceId = workspaceId,
                ProjectId = projectId,
                AnalysisId = analysisId,
                EventType = eventType,
                Message = message
            });
        }
    }
}
