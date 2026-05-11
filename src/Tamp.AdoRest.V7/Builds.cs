namespace Tamp.AdoRest.V7;

/// <summary>Endpoint group for <c>{project}/_apis/build/builds</c>.</summary>
public sealed class BuildsClient
{
    private readonly AdoRestClient _c;
    internal BuildsClient(AdoRestClient client) => _c = client;

    private static string Esc(string s) => Uri.EscapeDataString(s);

    /// <summary>POST <c>/_apis/build/builds</c> to queue a new run for a pipeline definition.</summary>
    public Task<Build> QueueAsync(string project, int definitionId, string? sourceBranch = null, IReadOnlyDictionary<string, string>? parameters = null, CancellationToken ct = default)
    {
        var body = new QueueBuildBody(
            Definition: new BuildDefinitionRef(definitionId),
            SourceBranch: sourceBranch,
            Parameters: parameters is null ? null : System.Text.Json.JsonSerializer.Serialize(parameters));
        return _c.PostInternal<Build>($"{Esc(project)}/_apis/build/builds?api-version=7.1", body, ct);
    }

    /// <summary>GET <c>/_apis/build/builds/{buildId}</c>.</summary>
    public Task<Build> GetByIdAsync(string project, int buildId, CancellationToken ct = default)
        => _c.GetInternal<Build>($"{Esc(project)}/_apis/build/builds/{buildId}?api-version=7.1", ct);

    /// <summary>GET <c>/_apis/build/builds?...</c> with optional filters. Returns the unwrapped list.</summary>
    public async Task<IReadOnlyList<Build>> ListAsync(string project, int? definitionId = null, string? statusFilter = null, string? resultFilter = null, int? top = null, CancellationToken ct = default)
    {
        var qs = new List<string> { "api-version=7.1" };
        if (definitionId is { } d) qs.Add($"definitions={d}");
        if (!string.IsNullOrEmpty(statusFilter)) qs.Add($"statusFilter={Esc(statusFilter)}");
        if (!string.IsNullOrEmpty(resultFilter)) qs.Add($"resultFilter={Esc(resultFilter)}");
        if (top is { } t) qs.Add($"$top={t}");
        var envelope = await _c.GetInternal<AdoCollection<Build>>(
            $"{Esc(project)}/_apis/build/builds?{string.Join('&', qs)}", ct).ConfigureAwait(false);
        return envelope.Value;
    }

    /// <summary>GET <c>/_apis/build/builds/{buildId}/timeline</c>.</summary>
    public Task<BuildTimeline> GetTimelineAsync(string project, int buildId, CancellationToken ct = default)
        => _c.GetInternal<BuildTimeline>($"{Esc(project)}/_apis/build/builds/{buildId}/timeline?api-version=7.1", ct);

    /// <summary>
    /// GET <c>/_apis/build/builds/{buildId}/logs/{logId}</c> as raw text. ADO returns the log
    /// as plain-text (NOT JSON) for this endpoint, so we hand-roll the request via the base
    /// client's raw escape hatch.
    /// </summary>
    public async Task<string> GetLogContentAsync(string project, int buildId, int logId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Esc(project)}/_apis/build/builds/{buildId}/logs/{logId}?api-version=7.1");
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/plain"));
        using var response = await _c.SendRawHttpAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new Http.ApiClientException(response.StatusCode, request.RequestUri?.ToString(), "GET", null,
                $"GET build log {logId} for build {buildId} -> {(int)response.StatusCode} {response.ReasonPhrase}");
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>Subset of ADO's Build DTO — the fields build scripts read most.</summary>
public sealed record Build(
    int Id,
    string BuildNumber,
    string Status,
    string? Result,
    DateTime QueueTime,
    DateTime? StartTime,
    DateTime? FinishTime,
    BuildDefinitionRef Definition,
    string SourceBranch,
    string SourceVersion,
    string? Url);

public sealed record BuildDefinitionRef(int Id, string? Name = null);

/// <summary>Timeline = list of records (jobs / phases / tasks within a build).</summary>
public sealed record BuildTimeline(string Id, int ChangeId, IReadOnlyList<BuildTimelineRecord> Records);

public sealed record BuildTimelineRecord(
    string Id,
    string? ParentId,
    string Type,
    string Name,
    string State,
    string? Result,
    DateTime? StartTime,
    DateTime? FinishTime,
    int? Order,
    int? LogId);

internal sealed record QueueBuildBody(BuildDefinitionRef Definition, string? SourceBranch, string? Parameters);
