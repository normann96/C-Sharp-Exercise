namespace CSharpApp.UnitTests.TestDoubles;

/// <summary>Scripted HttpMessageHandler: records every request (and body) and answers with the given responder.</summary>
public sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string?> CapturedBodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        CapturedBodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(ct));
        return responder(request);
    }
}
