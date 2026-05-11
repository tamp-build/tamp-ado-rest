using Tamp.Http;

namespace Tamp.AdoRest.V7;

/// <summary>
/// Typed wrapper for the Azure DevOps REST API 7.1. Construct once per
/// org with a PAT, navigate endpoint groups via the public properties.
///
/// <para>The PAT is typed as <see cref="Secret"/> — sent as HTTP Basic
/// with an empty username (standard ADO PAT auth), and joined to the
/// runner's redaction table so any log line that echoes the value is
/// scrubbed.</para>
///
/// <example>
/// <code>
/// using var ado = new AdoRestClient("https://dev.azure.com/i3solutions/", AdoPat);
/// var pr = await ado.PullRequests.GetByIdAsync("Strata", "Strata", id: 123);
/// </code>
/// </example>
/// </summary>
public sealed class AdoRestClient : TampApiClient
{
    /// <summary>The configured ADO organization URL (with trailing slash).</summary>
    public Uri OrganizationUrl => BaseUri;

    public AdoRestClient(string organizationUrl, Secret pat, bool disableConnectionVerification = false, HttpClient? http = null)
        : base(new Uri(organizationUrl ?? throw new ArgumentNullException(nameof(organizationUrl))),
               ApiCredential.BasicPat(pat),
               disableConnectionVerification,
               http,
               userAgent: "Tamp.AdoRest.V7/0.1.0")
    {
        PullRequests = new PullRequestsClient(this);
        Builds = new BuildsClient(this);
        ServiceEndpoints = new ServiceEndpointsClient(this);
    }

    /// <summary>Pull request operations.</summary>
    public PullRequestsClient PullRequests { get; }

    /// <summary>Build / pipeline operations.</summary>
    public BuildsClient Builds { get; }

    /// <summary>Service endpoint (service connection) operations.</summary>
    public ServiceEndpointsClient ServiceEndpoints { get; }

    /// <summary>Escape hatch — GET an arbitrary ADO endpoint as a typed result.</summary>
    public Task<T> GetRawAsync<T>(string relativeUri, CancellationToken ct = default) => base.GetAsync<T>(relativeUri, ct);

    /// <summary>Escape hatch — POST an arbitrary ADO endpoint with a JSON body and return a typed response.</summary>
    public Task<T> PostJsonRawAsync<T>(string relativeUri, object body, CancellationToken ct = default) => base.PostJsonAsync<T>(relativeUri, body, ct);

    /// <summary>Escape hatch — PATCH an arbitrary ADO endpoint with a JSON body.</summary>
    public Task<T> PatchJsonRawAsync<T>(string relativeUri, object body, CancellationToken ct = default) => base.PatchJsonAsync<T>(relativeUri, body, ct);

    /// <summary>Escape hatch — DELETE an arbitrary ADO endpoint.</summary>
    public Task DeleteRawAsync(string relativeUri, CancellationToken ct = default) => base.DeleteAsync(relativeUri, ct);

    /// <summary>Internal accessor for endpoint client classes to call the protected JSON helpers.</summary>
    internal Task<T> GetInternal<T>(string uri, CancellationToken ct) => base.GetAsync<T>(uri, ct);
    internal Task<T> PostInternal<T>(string uri, object body, CancellationToken ct) => base.PostJsonAsync<T>(uri, body, ct);
    internal Task PostInternal(string uri, object body, CancellationToken ct) => base.PostJsonAsync(uri, body, ct);
    internal Task<T> PatchInternal<T>(string uri, object body, CancellationToken ct) => base.PatchJsonAsync<T>(uri, body, ct);
    internal Task<HttpResponseMessage> SendRawHttpAsync(HttpRequestMessage request, CancellationToken ct) => base.SendRawAsync(request, ct: ct);
}

/// <summary>
/// Standard ADO envelope for list responses: <c>{ "value": [...], "count": N }</c>.
/// </summary>
public sealed record AdoCollection<T>(IReadOnlyList<T> Value, int Count);
