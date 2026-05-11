# Tamp.AdoRest

Library-mode wrapper for the **Azure DevOps REST API 7.1**. Built on
[Tamp.Http](https://github.com/tamp-build/tamp-http).

```csharp
using Tamp.AdoRest.V7;
```

| Package | ADO REST | Status |
|---|---|---|
| `Tamp.AdoRest.V7` | 7.1 | preview |

Requires `Tamp.Core ≥ 1.0.5` and `Tamp.Http ≥ 0.1.0`.

## Why library mode

ADO REST is an HTTP API, not a CLI binary. Other Tamp wrappers
(`Tamp.NetCli.V10`, `Tamp.Docker.V27`, etc.) emit `CommandPlan` for a
child process. `Tamp.AdoRest.V7` is the first library-mode wrapper —
async methods returning typed DTOs. Build scripts integrate via
`Executes(async () => ...)`.

PAT is typed as `Secret` and joins the runner's redaction table —
any log line that echoes the value gets scrubbed.

## Surface (v0.1.0)

| Group | Verbs |
|---|---|
| `PullRequests` | `GetByIdAsync`, `ListActiveAsync`, `CreateAsync`, `VoteAsync` (±10/±5/0), `CompleteAsync` (squash/merge/rebase) |
| `Builds` | `QueueAsync` (with parameters), `GetByIdAsync`, `ListAsync` (definition / status / result / top filters), `GetTimelineAsync`, `GetLogContentAsync` (raw text) |
| `ServiceEndpoints` | `ListAsync` (optional type filter), `GetByIdAsync`, `CreateWifAzureRmAsync` (the surface TAM-99 builds on) |

Plus generic escape hatches on the client itself:
`GetAsync<T>` · `PostJsonAsync<T>` · `PatchJsonAsync<T>` ·
`DeleteRawAsync`.

## Build-script example

```csharp
using Tamp;
using Tamp.AdoRest.V7;

[Secret("ADO PAT", EnvironmentVariable = "ADO_PAT")]
readonly Secret AdoPat = null!;

Target QueueBuildAndWatch => _ => _.Executes(async () =>
{
    using var ado = new AdoRestClient("https://dev.azure.com/i3solutions/", AdoPat);

    // Queue a run of pipeline 42 against main.
    var build = await ado.Builds.QueueAsync(
        project: "Strata",
        definitionId: 42,
        sourceBranch: "refs/heads/main");
    Console.WriteLine($"Queued build {build.Id} ({build.BuildNumber})");

    // Poll until completed.
    while (true)
    {
        var status = await ado.Builds.GetByIdAsync("Strata", build.Id);
        if (status.Status is "completed") {
            Console.WriteLine($"Build finished: {status.Result}");
            break;
        }
        await Task.Delay(TimeSpan.FromSeconds(15));
    }
});

Target ApprovePr => _ => _.Executes(async () =>
{
    using var ado = new AdoRestClient("https://dev.azure.com/i3solutions/", AdoPat);
    await ado.PullRequests.VoteAsync(
        project: "Strata", repository: "Strata",
        pullRequestId: PrNumber,
        reviewerId: ReviewerObjectId,
        vote: 10);
});
```

## Errors

Tamp.Http maps responses to typed exceptions:
- **4xx** → `ApiClientException` (`.IsClientError == true`) — don't
  retry without changing the request.
- **5xx** → `ApiServerException` (`.IsTransient == true`) — safe to
  retry with backoff.
- Network / DNS / TLS / cancellation → standard `HttpClient`
  exception types.

Error response body is captured into `ResponseBody` (truncated at 16
KiB by default).

## TLS / corporate proxy

Pass `disableConnectionVerification: true` to the constructor for
Zscaler-behind environments. Default is off (cert validation on).

## Not in v0.1.0

Hand-rolled via the escape hatches today; slated for v0.2.0 as typed
surface:
- `Environments.ListAsync` / `GetChecksAsync`
- `AgentPools.ListAsync` / `ListAgentsAsync` / `ListCapabilitiesAsync`
- `BranchPolicies.ListAsync` / `SetMinApproverCountAsync` / `SetRequiredReviewerAsync`
- `PullRequests.GetIterationChangesAsync`

## Releasing

See [MAINTAINERS.md](MAINTAINERS.md).
