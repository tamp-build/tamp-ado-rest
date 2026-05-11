using System.Net;
using System.Text;
using System.Text.Json;
using Tamp;
using Xunit;

namespace Tamp.AdoRest.V7.Tests;

public sealed class AdoRestClientTests
{
    private const string Org = "https://dev.azure.com/i3solutions/";

    private static (AdoRestClient Client, RecordingHandler Handler) MakeClient(
        Func<HttpRequestMessage, HttpResponseMessage>? responder = null)
    {
        var handler = new RecordingHandler();
        if (responder is not null) handler.Responder = responder;
        var http = new HttpClient(handler);
        var pat = new Secret("ADO PAT", "fake-pat-1234567890");
        var client = new AdoRestClient(Org, pat, http: http);
        return (client, handler);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object payload)
    {
        var resp = new HttpResponseMessage(status);
        resp.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return resp;
    }

    [Fact]
    public void Constructor_Rejects_Null_Organization_Url()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AdoRestClient(null!, new Secret("p", "x")));
    }

    [Fact]
    public void Constructor_Rejects_Null_Pat()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AdoRestClient(Org, null!));
    }

    [Fact]
    public async Task Every_Request_Carries_Basic_Pat_Auth()
    {
        var (client, handler) = MakeClient(
            responder: _ => JsonResponse(HttpStatusCode.OK, new
            {
                PullRequestId = 1,
                Status = "active",
                CreationDate = DateTime.UtcNow,
                Title = "x",
                SourceRefName = "refs/heads/x",
                TargetRefName = "refs/heads/main",
                IsDraft = false,
            }));

        await client.PullRequests.GetByIdAsync("Strata", "Strata", 1);

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes(":fake-pat-1234567890"));
        Assert.Equal($"Basic {expected}", handler.Requests[0].Headers["Authorization"][0]);
    }

    [Fact]
    public async Task User_Agent_Identifies_Tamp_AdoRest()
    {
        var (client, handler) = MakeClient(
            responder: _ => JsonResponse(HttpStatusCode.OK, new
            {
                PullRequestId = 1,
                Status = "active",
                CreationDate = DateTime.UtcNow,
                Title = "x",
                SourceRefName = "refs/heads/x",
                TargetRefName = "refs/heads/main",
                IsDraft = false,
            }));
        await client.PullRequests.GetByIdAsync("Strata", "Strata", 1);
        Assert.Contains("Tamp.AdoRest.V7", handler.Requests[0].Headers["User-Agent"][0]);
    }

    // ---- pull requests ----

    [Fact]
    public async Task PullRequests_GetById_Builds_Correct_Url()
    {
        var (client, handler) = MakeClient(
            responder: _ => JsonResponse(HttpStatusCode.OK, new
            {
                PullRequestId = 123,
                CodeReviewId = 42,
                Status = "active",
                CreationDate = DateTime.UtcNow,
                Title = "Test PR",
                SourceRefName = "refs/heads/feature/x",
                TargetRefName = "refs/heads/main",
                IsDraft = false,
            }));

        var pr = await client.PullRequests.GetByIdAsync("Strata", "Strata", 123);
        Assert.Equal(123, pr.PullRequestId);
        Assert.Equal(42, pr.CodeReviewId);

        var uri = handler.Requests[0].RequestUri!.AbsoluteUri;
        Assert.Contains("/Strata/_apis/git/repositories/Strata/pullrequests/123", uri);
        Assert.Contains("api-version=7.1", uri);
    }

    [Fact]
    public async Task PullRequests_ListActive_Adds_Search_Criteria()
    {
        var (client, handler) = MakeClient(
            responder: _ => JsonResponse(HttpStatusCode.OK, new
            {
                Value = new[]
                {
                    new {
                        PullRequestId = 1,
                        Status = "active",
                        CreationDate = DateTime.UtcNow,
                        Title = "PR 1",
                        SourceRefName = "refs/heads/x",
                        TargetRefName = "refs/heads/main",
                        IsDraft = false,
                    },
                },
                Count = 1,
            }));

        var prs = await client.PullRequests.ListActiveAsync("Strata", "Strata");
        Assert.Single(prs);
        Assert.Contains("searchCriteria.status=active", handler.Requests[0].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task PullRequests_Create_Posts_Json_Body()
    {
        var (client, handler) = MakeClient(
            responder: _ => JsonResponse(HttpStatusCode.Created, new
            {
                PullRequestId = 999,
                Status = "active",
                CreationDate = DateTime.UtcNow,
                Title = "New PR",
                SourceRefName = "refs/heads/feat",
                TargetRefName = "refs/heads/main",
                IsDraft = false,
            }));

        var pr = await client.PullRequests.CreateAsync(
            "Strata", "Strata",
            sourceRefName: "refs/heads/feat",
            targetRefName: "refs/heads/main",
            title: "New PR",
            description: "Description here",
            reviewerIds: new[] { 42, 43 });

        Assert.Equal(999, pr.PullRequestId);
        var body = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.Equal("refs/heads/feat", body.RootElement.GetProperty("sourceRefName").GetString());
        Assert.Equal("refs/heads/main", body.RootElement.GetProperty("targetRefName").GetString());
        Assert.Equal("New PR", body.RootElement.GetProperty("title").GetString());
        Assert.Equal("Description here", body.RootElement.GetProperty("description").GetString());

        var reviewers = body.RootElement.GetProperty("reviewers").EnumerateArray().ToArray();
        Assert.Equal(2, reviewers.Length);
        Assert.Equal("42", reviewers[0].GetProperty("id").GetString());
        Assert.Equal("43", reviewers[1].GetProperty("id").GetString());
    }

    [Theory]
    [InlineData(10)]
    [InlineData(5)]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(-10)]
    public async Task PullRequests_Vote_Accepts_Valid_Values(int vote)
    {
        var (client, handler) = MakeClient(
            responder: _ => JsonResponse(HttpStatusCode.OK, new { Id = "user-1", Vote = vote, IsRequired = false }));
        var result = await client.PullRequests.VoteAsync("Strata", "Strata", 1, "user-1", vote);
        Assert.Equal(vote, result.Vote);
        Assert.Equal(HttpMethod.Patch, handler.Requests[0].Method);
        Assert.Contains("/reviewers/user-1", handler.Requests[0].RequestUri!.AbsoluteUri);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(-1)]
    [InlineData(11)]
    public async Task PullRequests_Vote_Rejects_Invalid_Values(int vote)
    {
        var (client, _) = MakeClient();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            client.PullRequests.VoteAsync("Strata", "Strata", 1, "user-1", vote));
    }

    [Fact]
    public async Task PullRequests_Complete_Sends_Squash_Strategy()
    {
        var (client, handler) = MakeClient(
            responder: _ => JsonResponse(HttpStatusCode.OK, new
            {
                PullRequestId = 1,
                Status = "completed",
                CreationDate = DateTime.UtcNow,
                Title = "x",
                SourceRefName = "refs/heads/x",
                TargetRefName = "refs/heads/main",
                IsDraft = false,
            }));

        await client.PullRequests.CompleteAsync("Strata", "Strata", 1, "abc123", PullRequestMergeStrategy.Squash, deleteSourceBranch: true, mergeCommitMessage: "Merge");
        var body = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.Equal("completed", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("abc123", body.RootElement.GetProperty("lastMergeSourceCommit").GetProperty("commitId").GetString());
        var co = body.RootElement.GetProperty("completionOptions");
        Assert.Equal("squash", co.GetProperty("mergeStrategy").GetString());
        Assert.True(co.GetProperty("deleteSourceBranch").GetBoolean());
        Assert.Equal("Merge", co.GetProperty("mergeCommitMessage").GetString());
    }

    [Theory]
    [InlineData(PullRequestMergeStrategy.NoFastForward, "noFastForward")]
    [InlineData(PullRequestMergeStrategy.Squash, "squash")]
    [InlineData(PullRequestMergeStrategy.Rebase, "rebase")]
    [InlineData(PullRequestMergeStrategy.RebaseMerge, "rebaseMerge")]
    public async Task PullRequests_Complete_Strategy_Round_Trips(PullRequestMergeStrategy s, string wire)
    {
        var (client, handler) = MakeClient(
            responder: _ => JsonResponse(HttpStatusCode.OK, new
            {
                PullRequestId = 1,
                Status = "completed",
                CreationDate = DateTime.UtcNow,
                Title = "x",
                SourceRefName = "refs/heads/x",
                TargetRefName = "refs/heads/main",
                IsDraft = false,
            }));
        await client.PullRequests.CompleteAsync("Strata", "Strata", 1, "abc123", s);
        var co = JsonDocument.Parse(handler.Requests[0].Body!).RootElement.GetProperty("completionOptions");
        Assert.Equal(wire, co.GetProperty("mergeStrategy").GetString());
    }

    [Fact]
    public async Task PullRequests_GetById_4xx_Throws_ApiClientException()
    {
        var (client, _) = MakeClient(
            responder: _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"message\":\"Not Found\"}", Encoding.UTF8, "application/json"),
            });

        var ex = await Assert.ThrowsAsync<Tamp.Http.ApiClientException>(() =>
            client.PullRequests.GetByIdAsync("Strata", "Strata", 999));
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    // ---- builds ----

    [Fact]
    public async Task Builds_Queue_Posts_Definition_And_Branch()
    {
        var (client, handler) = MakeClient(
            responder: _ => JsonResponse(HttpStatusCode.OK, new
            {
                Id = 100,
                BuildNumber = "20260511.1",
                Status = "notStarted",
                QueueTime = DateTime.UtcNow,
                Definition = new { Id = 42, Name = "strata-api-ci" },
                SourceBranch = "refs/heads/main",
                SourceVersion = "abc",
            }));

        var build = await client.Builds.QueueAsync("Strata", 42, sourceBranch: "refs/heads/main");
        Assert.Equal(100, build.Id);
        Assert.Equal(42, build.Definition.Id);

        var body = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.Equal(42, body.RootElement.GetProperty("definition").GetProperty("id").GetInt32());
        Assert.Equal("refs/heads/main", body.RootElement.GetProperty("sourceBranch").GetString());
    }

    [Fact]
    public async Task Builds_Queue_Parameters_Are_Serialized_As_JSON_String()
    {
        var (client, handler) = MakeClient(
            responder: _ => JsonResponse(HttpStatusCode.OK, new
            {
                Id = 1,
                BuildNumber = "x",
                Status = "notStarted",
                QueueTime = DateTime.UtcNow,
                Definition = new { Id = 42 },
                SourceBranch = "refs/heads/main",
                SourceVersion = "x",
            }));

        await client.Builds.QueueAsync("Strata", 42, parameters: new Dictionary<string, string>
        {
            ["environment"] = "test",
            ["region"] = "eastus",
        });

        var body = JsonDocument.Parse(handler.Requests[0].Body!);
        var paramsStr = body.RootElement.GetProperty("parameters").GetString();
        // ADO requires parameters as a JSON-encoded string (double-encoded), not a nested object.
        Assert.NotNull(paramsStr);
        var inner = JsonDocument.Parse(paramsStr!);
        Assert.Equal("test", inner.RootElement.GetProperty("environment").GetString());
        Assert.Equal("eastus", inner.RootElement.GetProperty("region").GetString());
    }

    [Fact]
    public async Task Builds_List_Applies_Filters_To_Query_String()
    {
        var (client, handler) = MakeClient(
            responder: _ => JsonResponse(HttpStatusCode.OK, new { Value = Array.Empty<object>(), Count = 0 }));

        await client.Builds.ListAsync("Strata", definitionId: 42, statusFilter: "completed", resultFilter: "succeeded", top: 50);

        var uri = handler.Requests[0].RequestUri!.AbsoluteUri;
        Assert.Contains("definitions=42", uri);
        Assert.Contains("statusFilter=completed", uri);
        Assert.Contains("resultFilter=succeeded", uri);
        // ADO uses OData-style `$top` for pagination. `$` is a reserved
        // sub-delim character — Uri preserves it literally.
        Assert.Contains("$top=50", uri);
    }

    [Fact]
    public async Task Builds_GetTimeline_Builds_Correct_Url()
    {
        var (client, handler) = MakeClient(
            responder: _ => JsonResponse(HttpStatusCode.OK, new
            {
                Id = "timeline-1",
                ChangeId = 1,
                Records = Array.Empty<object>(),
            }));

        var timeline = await client.Builds.GetTimelineAsync("Strata", 100);
        Assert.Equal("timeline-1", timeline.Id);
        Assert.Contains("/builds/100/timeline", handler.Requests[0].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Builds_GetLogContent_Returns_Raw_Text()
    {
        var (client, handler) = MakeClient(
            responder: _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("2026-05-11T14:00:00.000Z Build started\n2026-05-11T14:00:01.000Z Step 1\n", Encoding.UTF8, "text/plain"),
            });

        var log = await client.Builds.GetLogContentAsync("Strata", 100, 5);
        Assert.Contains("Build started", log);
        Assert.Contains("/builds/100/logs/5", handler.Requests[0].RequestUri!.AbsoluteUri);
    }

    // ---- service endpoints ----

    [Fact]
    public async Task ServiceEndpoints_List_Returns_Unwrapped_Value()
    {
        var (client, _) = MakeClient(
            responder: _ => JsonResponse(HttpStatusCode.OK, new
            {
                Value = new[]
                {
                    new { Id = "ep-1", Name = "Test", Type = "azurerm", Url = "https://management.azure.com/", IsReady = true },
                },
                Count = 1,
            }));

        var endpoints = await client.ServiceEndpoints.ListAsync("Strata");
        Assert.Single(endpoints);
        Assert.Equal("ep-1", endpoints[0].Id);
        Assert.Equal("azurerm", endpoints[0].Type);
    }

    [Fact]
    public async Task ServiceEndpoints_List_Filter_By_Type()
    {
        var (client, handler) = MakeClient(
            responder: _ => JsonResponse(HttpStatusCode.OK, new { Value = Array.Empty<object>(), Count = 0 }));
        await client.ServiceEndpoints.ListAsync("Strata", type: "azurerm");
        Assert.Contains("type=azurerm", handler.Requests[0].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task ServiceEndpoints_CreateWif_Builds_Manual_Workload_Identity_Body()
    {
        // The shape of this body is exactly what az devops invoke produces
        // when you create a WIF SC interactively, validated against
        // Strata's prod test/prod SCs.
        var (client, handler) = MakeClient(
            responder: _ => JsonResponse(HttpStatusCode.OK, new
            {
                Id = "new-sc",
                Name = "sp-strata-cicd-test",
                Type = "azurerm",
                Url = "https://management.azure.com/",
                IsReady = true,
                Authorization = new
                {
                    Scheme = "WorkloadIdentityFederation",
                    Parameters = new { tenantid = "tid", serviceprincipalid = "cid", workloadIdentityFederationSubject = "sc-wif/subject" },
                },
            }));

        var sc = await client.ServiceEndpoints.CreateWifAzureRmAsync(
            project: "Strata",
            name: "sp-strata-cicd-test",
            subscriptionId: "sub-id",
            subscriptionName: "Strata Test",
            tenantId: "tid",
            servicePrincipalClientId: "cid");

        Assert.Equal("new-sc", sc.Id);

        var body = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.Equal("sp-strata-cicd-test", body.RootElement.GetProperty("name").GetString());
        Assert.Equal("azurerm", body.RootElement.GetProperty("type").GetString());
        Assert.Equal("WorkloadIdentityFederation", body.RootElement.GetProperty("authorization").GetProperty("scheme").GetString());
        var data = body.RootElement.GetProperty("data");
        Assert.Equal("sub-id", data.GetProperty("subscriptionId").GetString());
        Assert.Equal("Subscription", data.GetProperty("scopeLevel").GetString());
        Assert.Equal("Manual", data.GetProperty("creationMode").GetString());
    }

    // ---- raw escape hatch ----

    [Fact]
    public async Task GetRawAsync_Public_Escape_Hatch_Hits_Org_Scoped_Endpoint()
    {
        var (client, handler) = MakeClient(
            responder: _ => JsonResponse(HttpStatusCode.OK, new { Value = Array.Empty<object>() }));
        await client.GetRawAsync<Dictionary<string, object>>("_apis/distributedtask/pools?api-version=7.1");
        Assert.Contains("_apis/distributedtask/pools", handler.Requests[0].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task DeleteRawAsync_Hits_Expected_Endpoint()
    {
        var (client, handler) = MakeClient(
            responder: _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        await client.DeleteRawAsync("Strata/_apis/something/123?api-version=7.1");
        Assert.Equal(HttpMethod.Delete, handler.Requests[0].Method);
    }
}
