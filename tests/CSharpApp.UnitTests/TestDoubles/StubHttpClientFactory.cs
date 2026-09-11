namespace CSharpApp.UnitTests.TestDoubles;

/// <summary>Hands out the same client for every name, so a provider under test talks to a scripted handler.</summary>
public sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}
