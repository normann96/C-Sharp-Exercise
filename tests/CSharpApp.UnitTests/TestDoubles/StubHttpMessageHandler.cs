namespace CSharpApp.UnitTests.TestDoubles;

/// <summary>Scripted HttpMessageHandler: records every request (and body) and answers with the given responder.</summary>
public sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    private readonly Lock _recordLock = new();

    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string?> CapturedBodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);

        // Concurrency tests assert on these counts: a racing Add must not silently lose a request.
        lock (_recordLock)
        {
            Requests.Add(request);
            CapturedBodies.Add(body);
        }

        return responder(request);
    }
}
