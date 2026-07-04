using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SpendGovernor.Core;
using PersistenceRepository = SpendGovernor.Infrastructure.Persistence.Repository;

public enum GitHubIntegrationMode
{
    Simulated,
    Real
}

public sealed class GitHubIntegrationOptions
{
    public GitHubIntegrationMode Mode { get; set; } = GitHubIntegrationMode.Simulated;
    public string AppId { get; set; } = "";
    public string PrivateKeyPath { get; set; } = "";
    public string PrivateKey { get; set; } = "";
    public string WebhookSecret { get; set; } = "dev-secret";
    public bool EnableCheckRuns { get; set; } = true;
    public string BotCommentMarker { get; set; } = PrCommentRenderer.Marker;
    public bool AllowUnsignedWebhooksInDevelopment { get; set; }
}

public static class GitHubReporterFactory
{
    public static bool ShouldUseReal(GitHubIntegrationOptions options)
    {
        return options.Mode == GitHubIntegrationMode.Real;
    }

    public static IGitHubPullRequestReporter Create(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<GitHubIntegrationOptions>>().Value;
        return ShouldUseReal(options)
            ? new RealGitHubPullRequestReporter(
                services.GetRequiredService<IGitHubApiClient>(),
                services.GetRequiredService<IOptions<GitHubIntegrationOptions>>(),
                services.GetRequiredService<SpendGovernorStore>())
            : new SimulatedGitHubPullRequestReporter(services.GetRequiredService<SpendGovernorStore>());
    }
}

public sealed record GitHubReportPublishRequest(
    Project Project,
    PersistenceRepository Repository,
    Guid ScanId,
    AnalysisResult Result,
    AnalysisRequest AnalysisRequest,
    string? ExistingCommentId,
    string? ExistingCheckRunId);

public sealed class GitHubReportPublishResult
{
    public string PublishingStatus { get; init; } = "Pending";
    public string? CommentId { get; init; }
    public string? CheckRunId { get; init; }
    public string? ReportUrl { get; init; }
    public string? ErrorMessage { get; init; }
    public bool IsSimulated { get; init; }
    public bool? WasCreatedOnLastWrite { get; init; }
    public int? UpdateCount { get; init; }
    public bool Succeeded => PublishingStatus is "Published" or "Simulated";

    public static GitHubReportPublishResult Simulated(GitHubPrCommentState comment, string? reportUrl)
    {
        return new GitHubReportPublishResult
        {
            PublishingStatus = "Simulated",
            CommentId = comment.CommentId.ToString(CultureInfo.InvariantCulture),
            ReportUrl = reportUrl,
            IsSimulated = true,
            WasCreatedOnLastWrite = comment.WasCreatedOnLastWrite,
            UpdateCount = comment.UpdateCount
        };
    }

    public static GitHubReportPublishResult Published(string? commentId, string? checkRunId, string? reportUrl)
    {
        return new GitHubReportPublishResult
        {
            PublishingStatus = "Published",
            CommentId = commentId,
            CheckRunId = checkRunId,
            ReportUrl = reportUrl
        };
    }

    public static GitHubReportPublishResult Failed(string? commentId, string? checkRunId, string? reportUrl, string errorMessage)
    {
        return new GitHubReportPublishResult
        {
            PublishingStatus = "Failed",
            CommentId = commentId,
            CheckRunId = checkRunId,
            ReportUrl = reportUrl,
            ErrorMessage = errorMessage
        };
    }
}

public interface IGitHubPullRequestReporter
{
    Task<GitHubReportPublishResult> PublishAsync(GitHubReportPublishRequest request, CancellationToken cancellationToken = default);
}

public sealed class SimulatedGitHubPullRequestReporter : IGitHubPullRequestReporter
{
    private readonly SpendGovernorStore store;

    public SimulatedGitHubPullRequestReporter(SpendGovernorStore store)
    {
        this.store = store;
    }

    public Task<GitHubReportPublishResult> PublishAsync(GitHubReportPublishRequest request, CancellationToken cancellationToken = default)
    {
        var comment = store.SaveAnalysis(request.Project, request.Result, request.AnalysisRequest, request.ExistingCommentId);
        return Task.FromResult(GitHubReportPublishResult.Simulated(
            comment,
            request.Result.DashboardUrl ?? request.Result.Analysis.DashboardUrl));
    }
}

public sealed class RealGitHubPullRequestReporter : IGitHubPullRequestReporter
{
    public const string CheckRunName = "Cloud & AI Spend Governor";

    private readonly IGitHubApiClient client;
    private readonly GitHubIntegrationOptions options;
    private readonly SpendGovernorStore store;

    public RealGitHubPullRequestReporter(IGitHubApiClient client, IOptions<GitHubIntegrationOptions> options, SpendGovernorStore store)
    {
        this.client = client;
        this.options = options.Value;
        this.store = store;
    }

    public async Task<GitHubReportPublishResult> PublishAsync(GitHubReportPublishRequest request, CancellationToken cancellationToken = default)
    {
        store.RecordAnalysis(request.Project, request.Result, request.AnalysisRequest);

        var installationId = request.Repository.InstallationId ?? request.Project.GitHubInstallationId;
        if (string.IsNullOrWhiteSpace(installationId))
        {
            return GitHubReportPublishResult.Failed(
                request.ExistingCommentId,
                request.ExistingCheckRunId,
                request.Result.DashboardUrl ?? request.Result.Analysis.DashboardUrl,
                "GitHub publishing is in Real mode, but no GitHub installation id is linked to this repository.");
        }

        try
        {
            var marker = string.IsNullOrWhiteSpace(options.BotCommentMarker)
                ? PrCommentRenderer.Marker
                : options.BotCommentMarker;
            var body = EnsureMarker(request.Result.CommentMarkdown, marker);
            var comment = await UpsertCommentAsync(request, installationId, marker, body, cancellationToken);
            GitHubCheckRun? checkRun = null;

            if (options.EnableCheckRuns && !string.IsNullOrWhiteSpace(request.AnalysisRequest.CommitSha))
            {
                checkRun = await UpsertCheckRunAsync(request, installationId, cancellationToken);
            }

            return GitHubReportPublishResult.Published(
                comment.Id.ToString(CultureInfo.InvariantCulture),
                checkRun?.Id.ToString(CultureInfo.InvariantCulture),
                comment.HtmlUrl ?? checkRun?.HtmlUrl ?? request.Result.DashboardUrl ?? request.Result.Analysis.GitHubPullRequestUrl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return GitHubReportPublishResult.Failed(
                request.ExistingCommentId,
                request.ExistingCheckRunId,
                request.Result.DashboardUrl ?? request.Result.Analysis.DashboardUrl,
                ex.Message);
        }
    }

    public static string ToCheckRunConclusion(AnalysisResult result)
    {
        if (result.Analysis.Status == AnalysisStatus.Failed)
        {
            return "action_required";
        }

        return result.Analysis.PolicyStatus switch
        {
            PolicyAction.Warn => "neutral",
            PolicyAction.ApprovalRequired => "failure",
            PolicyAction.Block => "failure",
            _ => "success"
        };
    }

    private async Task<GitHubIssueComment> UpsertCommentAsync(
        GitHubReportPublishRequest request,
        string installationId,
        string marker,
        string body,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.ExistingCommentId))
        {
            try
            {
                return await client.UpdateIssueCommentAsync(
                    request.Repository.Owner,
                    request.Repository.Name,
                    installationId,
                    request.ExistingCommentId,
                    body,
                    cancellationToken);
            }
            catch (GitHubNotFoundException)
            {
                // The old stored comment can be deleted manually; fall through to marker lookup.
            }
        }

        var comments = await client.ListIssueCommentsAsync(
            request.Repository.Owner,
            request.Repository.Name,
            installationId,
            request.AnalysisRequest.PullRequestNumber,
            cancellationToken);
        var existing = comments.FirstOrDefault(comment =>
            comment.Body?.Contains(marker, StringComparison.Ordinal) == true);
        if (existing is not null)
        {
            return await client.UpdateIssueCommentAsync(
                request.Repository.Owner,
                request.Repository.Name,
                installationId,
                existing.Id.ToString(CultureInfo.InvariantCulture),
                body,
                cancellationToken);
        }

        return await client.CreateIssueCommentAsync(
            request.Repository.Owner,
            request.Repository.Name,
            installationId,
            request.AnalysisRequest.PullRequestNumber,
            body,
            cancellationToken);
    }

    private async Task<GitHubCheckRun> UpsertCheckRunAsync(
        GitHubReportPublishRequest request,
        string installationId,
        CancellationToken cancellationToken)
    {
        var conclusion = ToCheckRunConclusion(request.Result);
        var output = new GitHubCheckRunOutput(
            CheckRunName,
            BuildCheckRunSummary(request.Result),
            request.Result.CommentMarkdown);

        if (!string.IsNullOrWhiteSpace(request.ExistingCheckRunId))
        {
            try
            {
                return await client.UpdateCheckRunAsync(
                    request.Repository.Owner,
                    request.Repository.Name,
                    installationId,
                    request.ExistingCheckRunId,
                    conclusion,
                    request.Result.DashboardUrl ?? request.Result.Analysis.DashboardUrl,
                    output,
                    cancellationToken);
            }
            catch (GitHubNotFoundException)
            {
                // The previous check run no longer exists; create a fresh completed run.
            }
        }

        return await client.CreateCheckRunAsync(
            request.Repository.Owner,
            request.Repository.Name,
            installationId,
            request.AnalysisRequest.CommitSha,
            CheckRunName,
            conclusion,
            request.Result.DashboardUrl ?? request.Result.Analysis.DashboardUrl,
            output,
            cancellationToken);
    }

    private static string EnsureMarker(string body, string marker)
    {
        if (body.Contains(marker, StringComparison.Ordinal))
        {
            return body;
        }

        if (body.Contains(PrCommentRenderer.Marker, StringComparison.Ordinal))
        {
            return body.Replace(PrCommentRenderer.Marker, marker, StringComparison.Ordinal);
        }

        return marker + Environment.NewLine + body;
    }

    private static string BuildCheckRunSummary(AnalysisResult result)
    {
        var analysis = result.Analysis;
        var delta = analysis.MonthlyDelta is null
            ? "unknown monthly delta"
            : string.Create(CultureInfo.InvariantCulture, $"{analysis.MonthlyDelta:0.##} {analysis.Currency}/month");
        return $"{analysis.PolicyStatus} with {delta} and {analysis.OverallConfidence} confidence.";
    }
}

public interface IGitHubApiClient
{
    Task<IReadOnlyList<GitHubIssueComment>> ListIssueCommentsAsync(
        string owner,
        string repository,
        string installationId,
        int pullRequestNumber,
        CancellationToken cancellationToken = default);

    Task<GitHubIssueComment> CreateIssueCommentAsync(
        string owner,
        string repository,
        string installationId,
        int pullRequestNumber,
        string body,
        CancellationToken cancellationToken = default);

    Task<GitHubIssueComment> UpdateIssueCommentAsync(
        string owner,
        string repository,
        string installationId,
        string commentId,
        string body,
        CancellationToken cancellationToken = default);

    Task<GitHubCheckRun> CreateCheckRunAsync(
        string owner,
        string repository,
        string installationId,
        string headSha,
        string name,
        string conclusion,
        string? detailsUrl,
        GitHubCheckRunOutput output,
        CancellationToken cancellationToken = default);

    Task<GitHubCheckRun> UpdateCheckRunAsync(
        string owner,
        string repository,
        string installationId,
        string checkRunId,
        string conclusion,
        string? detailsUrl,
        GitHubCheckRunOutput output,
        CancellationToken cancellationToken = default);
}

public sealed class GitHubIssueComment
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }
}

public sealed class GitHubCheckRun
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }
}

public sealed record GitHubCheckRunOutput(string Title, string Summary, string Text);

public sealed class GitHubNotFoundException : Exception
{
    public GitHubNotFoundException(string message)
        : base(message)
    {
    }
}

public sealed class GitHubApiException : Exception
{
    public GitHubApiException(string message)
        : base(message)
    {
    }
}

public sealed class GitHubApiClient : IGitHubApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory httpClientFactory;
    private readonly IOptions<GitHubIntegrationOptions> options;
    private readonly object tokenGate = new();
    private readonly Dictionary<string, CachedInstallationToken> tokens = [];

    public GitHubApiClient(IHttpClientFactory httpClientFactory, IOptions<GitHubIntegrationOptions> options)
    {
        this.httpClientFactory = httpClientFactory;
        this.options = options;
    }

    public async Task<IReadOnlyList<GitHubIssueComment>> ListIssueCommentsAsync(
        string owner,
        string repository,
        string installationId,
        int pullRequestNumber,
        CancellationToken cancellationToken = default)
    {
        var token = await GetInstallationTokenAsync(installationId, cancellationToken);
        var comments = await SendGitHubAsync<GitHubIssueComment[]>(
            HttpMethod.Get,
            ApiUrl("repos", owner, repository, "issues", pullRequestNumber.ToString(CultureInfo.InvariantCulture), "comments") + "?per_page=100",
            token,
            null,
            cancellationToken);
        return comments;
    }

    public async Task<GitHubIssueComment> CreateIssueCommentAsync(
        string owner,
        string repository,
        string installationId,
        int pullRequestNumber,
        string body,
        CancellationToken cancellationToken = default)
    {
        var token = await GetInstallationTokenAsync(installationId, cancellationToken);
        return await SendGitHubAsync<GitHubIssueComment>(
            HttpMethod.Post,
            ApiUrl("repos", owner, repository, "issues", pullRequestNumber.ToString(CultureInfo.InvariantCulture), "comments"),
            token,
            new { body },
            cancellationToken);
    }

    public async Task<GitHubIssueComment> UpdateIssueCommentAsync(
        string owner,
        string repository,
        string installationId,
        string commentId,
        string body,
        CancellationToken cancellationToken = default)
    {
        var token = await GetInstallationTokenAsync(installationId, cancellationToken);
        return await SendGitHubAsync<GitHubIssueComment>(
            HttpMethod.Patch,
            ApiUrl("repos", owner, repository, "issues", "comments", commentId),
            token,
            new { body },
            cancellationToken);
    }

    public async Task<GitHubCheckRun> CreateCheckRunAsync(
        string owner,
        string repository,
        string installationId,
        string headSha,
        string name,
        string conclusion,
        string? detailsUrl,
        GitHubCheckRunOutput output,
        CancellationToken cancellationToken = default)
    {
        var token = await GetInstallationTokenAsync(installationId, cancellationToken);
        return await SendGitHubAsync<GitHubCheckRun>(
            HttpMethod.Post,
            ApiUrl("repos", owner, repository, "check-runs"),
            token,
            new
            {
                name,
                head_sha = headSha,
                status = "completed",
                conclusion,
                details_url = detailsUrl,
                completed_at = DateTimeOffset.UtcNow,
                output = new
                {
                    title = output.Title,
                    summary = output.Summary,
                    text = output.Text
                }
            },
            cancellationToken);
    }

    public async Task<GitHubCheckRun> UpdateCheckRunAsync(
        string owner,
        string repository,
        string installationId,
        string checkRunId,
        string conclusion,
        string? detailsUrl,
        GitHubCheckRunOutput output,
        CancellationToken cancellationToken = default)
    {
        var token = await GetInstallationTokenAsync(installationId, cancellationToken);
        return await SendGitHubAsync<GitHubCheckRun>(
            HttpMethod.Patch,
            ApiUrl("repos", owner, repository, "check-runs", checkRunId),
            token,
            new
            {
                status = "completed",
                conclusion,
                details_url = detailsUrl,
                completed_at = DateTimeOffset.UtcNow,
                output = new
                {
                    title = output.Title,
                    summary = output.Summary,
                    text = output.Text
                }
            },
            cancellationToken);
    }

    private async Task<string> GetInstallationTokenAsync(string installationId, CancellationToken cancellationToken)
    {
        lock (tokenGate)
        {
            if (tokens.TryGetValue(installationId, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
            {
                return cached.Token;
            }
        }

        var appJwt = CreateAppJwt(options.Value);
        var response = await SendGitHubAsync<InstallationTokenResponse>(
            HttpMethod.Post,
            ApiUrl("app", "installations", installationId, "access_tokens"),
            appJwt,
            new { },
            cancellationToken);

        if (string.IsNullOrWhiteSpace(response.Token))
        {
            throw new GitHubApiException("GitHub did not return an installation token.");
        }

        lock (tokenGate)
        {
            tokens[installationId] = new CachedInstallationToken(response.Token, response.ExpiresAt);
        }

        return response.Token;
    }

    private async Task<T> SendGitHubAsync<T>(HttpMethod method, string url, string bearerToken, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.UserAgent.ParseAdd("CloudAISpendGovernor/0.1");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new GitHubNotFoundException($"GitHub resource was not found: {url}");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new GitHubApiException($"GitHub API returned {(int)response.StatusCode} {response.ReasonPhrase}: {ExtractMessage(content)}");
        }

        var value = JsonSerializer.Deserialize<T>(content, JsonOptions);
        return value is null
            ? throw new GitHubApiException("GitHub returned an empty response.")
            : value;
    }

    private static string CreateAppJwt(GitHubIntegrationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AppId))
        {
            throw new GitHubApiException("GitHub:AppId is required when GitHub:Mode is Real.");
        }

        var now = DateTimeOffset.UtcNow.AddSeconds(-30);
        var header = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var payload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iat = now.ToUnixTimeSeconds(),
            exp = now.AddMinutes(9).ToUnixTimeSeconds(),
            iss = options.AppId
        }));
        var unsignedToken = $"{header}.{payload}";
        using var rsa = RSA.Create();
        rsa.ImportFromPem(LoadPrivateKey(options));
        var signature = rsa.SignData(Encoding.ASCII.GetBytes(unsignedToken), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{unsignedToken}.{Base64UrlEncode(signature)}";
    }

    private static string LoadPrivateKey(GitHubIntegrationOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.PrivateKey))
        {
            return options.PrivateKey.Replace("\\n", "\n", StringComparison.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(options.PrivateKeyPath))
        {
            return File.ReadAllText(options.PrivateKeyPath);
        }

        throw new GitHubApiException("GitHub:PrivateKeyPath or GitHub:PrivateKey is required when GitHub:Mode is Real.");
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string ApiUrl(params string[] segments)
    {
        return "https://api.github.com/" + string.Join('/', segments.Select(Uri.EscapeDataString));
    }

    private static string ExtractMessage(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "No response body.";
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            return document.RootElement.TryGetProperty("message", out var message)
                ? message.GetString() ?? "No message."
                : "No message.";
        }
        catch (JsonException)
        {
            return "Response was not valid JSON.";
        }
    }

    private sealed record CachedInstallationToken(string Token, DateTimeOffset ExpiresAt);

    private sealed class InstallationTokenResponse
    {
        [JsonPropertyName("token")]
        public string Token { get; set; } = "";

        [JsonPropertyName("expires_at")]
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
