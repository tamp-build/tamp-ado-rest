using System.Net;
using System.Net.Http.Headers;

namespace Tamp.AdoRest.V7.Tests;

/// <summary>
/// HttpMessageHandler that records every outgoing request and returns
/// scripted responses. Same pattern as Tamp.Http's test recorder —
/// duplicated here because the type is internal in Tamp.Http.Tests.
/// </summary>
internal sealed class RecordingHandler : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = [];
    public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } =
        _ => new HttpResponseMessage(HttpStatusCode.NoContent);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri,
            request.Headers.ToDictionary(h => h.Key, h => h.Value.ToList(), StringComparer.OrdinalIgnoreCase),
            request.Content?.Headers.ContentType,
            body));
        return Responder(request);
    }
}

internal sealed record RecordedRequest(
    HttpMethod Method,
    Uri? RequestUri,
    IDictionary<string, List<string>> Headers,
    MediaTypeHeaderValue? ContentType,
    string? Body);
