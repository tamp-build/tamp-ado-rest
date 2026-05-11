namespace Tamp.AdoRest.V7;

/// <summary>Endpoint group for <c>/_apis/serviceendpoint/endpoints</c>. The TAM-99 wrapper (programmatic WIF SC creation) builds on top.</summary>
public sealed class ServiceEndpointsClient
{
    private readonly AdoRestClient _c;
    internal ServiceEndpointsClient(AdoRestClient client) => _c = client;

    private static string Esc(string s) => Uri.EscapeDataString(s);

    /// <summary>GET <c>/_apis/serviceendpoint/endpoints?...</c> scoped to a project.</summary>
    public async Task<IReadOnlyList<ServiceEndpoint>> ListAsync(string project, string? type = null, CancellationToken ct = default)
    {
        var qs = new List<string> { "api-version=7.1" };
        if (!string.IsNullOrEmpty(type)) qs.Add($"type={Esc(type)}");
        var envelope = await _c.GetInternal<AdoCollection<ServiceEndpoint>>(
            $"{Esc(project)}/_apis/serviceendpoint/endpoints?{string.Join('&', qs)}", ct).ConfigureAwait(false);
        return envelope.Value;
    }

    /// <summary>GET a single service endpoint by id.</summary>
    public Task<ServiceEndpoint> GetByIdAsync(string project, string endpointId, CancellationToken ct = default)
        => _c.GetInternal<ServiceEndpoint>($"{Esc(project)}/_apis/serviceendpoint/endpoints/{Esc(endpointId)}?api-version=7.1", ct);

    /// <summary>
    /// POST <c>/_apis/serviceendpoint/endpoints?api-version=7.1</c> to create a Workload-Identity-Federation
    /// Azure Resource Manager service connection. Returned object contains the WIF subject identifier ADO
    /// generated, which the caller then wires up via <c>az ad app federated-credential create</c>.
    /// </summary>
    /// <param name="project">Project name or id that the SC scopes to.</param>
    /// <param name="name">Display name for the new SC.</param>
    /// <param name="subscriptionId">Azure subscription GUID.</param>
    /// <param name="subscriptionName">Subscription display name.</param>
    /// <param name="tenantId">Entra tenant GUID.</param>
    /// <param name="servicePrincipalClientId">Existing Entra app client ID — caller creates the SP first.</param>
    /// <param name="creationMode">Use <c>"Manual"</c> when the SP exists already; <c>"Automatic"</c> when ADO should provision it.</param>
    public Task<ServiceEndpoint> CreateWifAzureRmAsync(
        string project,
        string name,
        string subscriptionId,
        string subscriptionName,
        string tenantId,
        string servicePrincipalClientId,
        string creationMode = "Manual",
        CancellationToken ct = default)
    {
        var body = new CreateServiceEndpointBody(
            Name: name,
            Type: "azurerm",
            Url: "https://management.azure.com/",
            Authorization: new ServiceEndpointAuthorization(
                Scheme: "WorkloadIdentityFederation",
                Parameters: new Dictionary<string, string>
                {
                    ["tenantid"] = tenantId,
                    ["serviceprincipalid"] = servicePrincipalClientId,
                }),
            Data: new Dictionary<string, string>
            {
                ["subscriptionId"] = subscriptionId,
                ["subscriptionName"] = subscriptionName,
                ["environment"] = "AzureCloud",
                ["scopeLevel"] = "Subscription",
                ["creationMode"] = creationMode,
            },
            IsShared: false,
            IsReady: true,
            ServiceEndpointProjectReferences: new[]
            {
                new ServiceEndpointProjectRef(
                    Name: name,
                    ProjectReference: new ProjectReference(name: project))
            });
        return _c.PostInternal<ServiceEndpoint>($"_apis/serviceendpoint/endpoints?api-version=7.1", body, ct);
    }
}

/// <summary>Subset of ADO's ServiceEndpoint DTO.</summary>
public sealed record ServiceEndpoint(
    string Id,
    string Name,
    string Type,
    string Url,
    bool? IsShared,
    bool? IsReady,
    ServiceEndpointAuthorization? Authorization,
    IDictionary<string, string>? Data,
    IReadOnlyList<ServiceEndpointProjectRef>? ServiceEndpointProjectReferences);

public sealed record ServiceEndpointAuthorization(string Scheme, IDictionary<string, string> Parameters);

public sealed record ServiceEndpointProjectRef(string Name, ProjectReference ProjectReference);

public sealed record ProjectReference(string? Id = null, string? name = null);

internal sealed record CreateServiceEndpointBody(
    string Name,
    string Type,
    string Url,
    ServiceEndpointAuthorization Authorization,
    IDictionary<string, string> Data,
    bool IsShared,
    bool IsReady,
    IReadOnlyList<ServiceEndpointProjectRef> ServiceEndpointProjectReferences);
