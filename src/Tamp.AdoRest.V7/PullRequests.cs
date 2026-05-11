namespace Tamp.AdoRest.V7;

/// <summary>Endpoint group for <c>{project}/_apis/git/repositories/{repo}/pullrequests</c>.</summary>
public sealed class PullRequestsClient
{
    private readonly AdoRestClient _c;
    internal PullRequestsClient(AdoRestClient client) => _c = client;

    private static string Base(string project, string repo) =>
        $"{Esc(project)}/_apis/git/repositories/{Esc(repo)}/pullrequests";

    private static string Esc(string s) => Uri.EscapeDataString(s);

    /// <summary>GET <c>/_apis/git/repositories/{repo}/pullrequests/{id}</c>.</summary>
    public Task<PullRequest> GetByIdAsync(string project, string repository, int pullRequestId, CancellationToken ct = default)
        => _c.GetInternal<PullRequest>($"{Base(project, repository)}/{pullRequestId}?api-version=7.1", ct);

    /// <summary>GET <c>/_apis/git/repositories/{repo}/pullrequests?searchCriteria.status=active</c>. Returns the unwrapped value list.</summary>
    public async Task<IReadOnlyList<PullRequest>> ListActiveAsync(string project, string repository, int? top = null, CancellationToken ct = default)
    {
        var top_ = top is null ? string.Empty : $"&$top={top}";
        var envelope = await _c.GetInternal<AdoCollection<PullRequest>>(
            $"{Base(project, repository)}?searchCriteria.status=active&api-version=7.1{top_}", ct).ConfigureAwait(false);
        return envelope.Value;
    }

    /// <summary>POST <c>/_apis/git/repositories/{repo}/pullrequests</c>. Required: source + target refs, title. Optional: description.</summary>
    public Task<PullRequest> CreateAsync(string project, string repository, string sourceRefName, string targetRefName, string title, string? description = null, IReadOnlyList<int>? reviewerIds = null, CancellationToken ct = default)
    {
        var body = new CreatePullRequestBody(
            SourceRefName: sourceRefName,
            TargetRefName: targetRefName,
            Title: title,
            Description: description,
            Reviewers: reviewerIds?.Select(id => new IdentityRef(id.ToString())).ToArray());
        return _c.PostInternal<PullRequest>($"{Base(project, repository)}?api-version=7.1", body, ct);
    }

    /// <summary>
    /// PUT <c>/_apis/git/repositories/{repo}/pullrequests/{id}/reviewers/{reviewerId}</c>.
    /// Vote semantics: 10 = approved, 5 = approved with suggestions, 0 = no vote, -5 = waiting, -10 = rejected.
    /// </summary>
    public Task<ReviewerVote> VoteAsync(string project, string repository, int pullRequestId, string reviewerId, int vote, CancellationToken ct = default)
    {
        if (vote != 10 && vote != 5 && vote != 0 && vote != -5 && vote != -10)
            throw new ArgumentOutOfRangeException(nameof(vote), vote, "Vote must be one of: 10, 5, 0, -5, -10.");
        return _c.PatchInternal<ReviewerVote>(
            $"{Base(project, repository)}/{pullRequestId}/reviewers/{Esc(reviewerId)}?api-version=7.1",
            new VoteBody(vote), ct);
    }

    /// <summary>
    /// PATCH <c>/_apis/git/repositories/{repo}/pullrequests/{id}</c> to complete (squash/merge/rebase).
    /// </summary>
    public Task<PullRequest> CompleteAsync(string project, string repository, int pullRequestId, string lastMergeSourceCommit, PullRequestMergeStrategy mergeStrategy = PullRequestMergeStrategy.Squash, bool deleteSourceBranch = true, string? mergeCommitMessage = null, CancellationToken ct = default)
    {
        var body = new CompletePullRequestBody(
            Status: "completed",
            LastMergeSourceCommit: new GitCommitRef(lastMergeSourceCommit),
            CompletionOptions: new CompletionOptions(
                MergeStrategy: mergeStrategy.ToWireValue(),
                DeleteSourceBranch: deleteSourceBranch,
                MergeCommitMessage: mergeCommitMessage));
        return _c.PatchInternal<PullRequest>(
            $"{Base(project, repository)}/{pullRequestId}?api-version=7.1", body, ct);
    }
}

/// <summary>Subset of ADO's PullRequest DTO — surfaces the fields most build scripts read.</summary>
public sealed record PullRequest(
    int PullRequestId,
    int? CodeReviewId,
    string Status,
    IdentityRef? CreatedBy,
    DateTime CreationDate,
    string Title,
    string? Description,
    string SourceRefName,
    string TargetRefName,
    string? MergeStatus,
    bool IsDraft,
    string? Url);

/// <summary>Simplified identity reference — ADO's full IdentityRef has more fields but build scripts rarely need them.</summary>
public sealed record IdentityRef(string Id, string? DisplayName = null, string? UniqueName = null, string? Url = null);

/// <summary>Outcome of a vote PATCH.</summary>
public sealed record ReviewerVote(string Id, int Vote, bool? IsRequired);

/// <summary>Merge strategies supported by ADO's "complete" path.</summary>
public enum PullRequestMergeStrategy
{
    NoFastForward,
    Squash,
    Rebase,
    RebaseMerge,
}

internal static class PullRequestMergeStrategyExtensions
{
    public static string ToWireValue(this PullRequestMergeStrategy s) => s switch
    {
        PullRequestMergeStrategy.NoFastForward => "noFastForward",
        PullRequestMergeStrategy.Squash => "squash",
        PullRequestMergeStrategy.Rebase => "rebase",
        PullRequestMergeStrategy.RebaseMerge => "rebaseMerge",
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, null),
    };
}

// ---- request bodies (internal so we can change shape without breaking consumers) ----

internal sealed record CreatePullRequestBody(string SourceRefName, string TargetRefName, string Title, string? Description, IdentityRef[]? Reviewers);
internal sealed record VoteBody(int Vote);
internal sealed record CompletePullRequestBody(string Status, GitCommitRef LastMergeSourceCommit, CompletionOptions CompletionOptions);
internal sealed record GitCommitRef(string CommitId);
internal sealed record CompletionOptions(string MergeStrategy, bool DeleteSourceBranch, string? MergeCommitMessage);
