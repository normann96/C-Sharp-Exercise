using CSharpApp.Api.Middleware;
using CSharpApp.Core.Settings;
using CSharpApp.IntegrationTests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CSharpApp.IntegrationTests.Common;

/// <summary>
/// Shared pieces for tests that drive the booted application over HTTP. One fixture (one host) per test class;
/// the tests of a class run one after another, so the fake's recordings and the captured log belong to the
/// current test from the moment it was constructed.
/// </summary>
public abstract class ApiTestBase : IClassFixture<ApiFixture>
{
    protected const string ProductsRoute = "/api/v1/products";
    protected const string CategoriesRoute = "/api/v1/categories";
    protected const string ProblemContentType = "application/problem+json";
    protected const string ServerTimingPattern = @"^total;dur=\d+\.\d$";

    private static readonly string TimingCategory = typeof(RequestTimingMiddleware).FullName!;
    private readonly int _logMark;

    protected ApiTestBase(ApiFixture fixture)
    {
        Fixture = fixture;
        Client = fixture.CreateClient();
        fixture.Fake.ResetScript(); // a test that failed half-way must not hand its scripted failures to the next one
        _logMark = fixture.Logs.Count;
    }

    protected ApiFixture Fixture { get; }
    protected HttpClient Client { get; }
    protected FakePlatziHandler Fake => Fixture.Fake;

    /// <summary>One attempt plus the configured retries: what the upstream sees for a call that keeps failing.</summary>
    protected int UpstreamAttempts => Fixture.Services.GetRequiredService<IOptions<HttpClientSettings>>().Value.RetryCount + 1;

    protected IReadOnlyList<CapturedLogEvent> TimingEvents()
        => Fixture.Logs.Events.Skip(_logMark).Where(captured => captured.Category == TimingCategory).ToList();

    protected static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    protected static JsonElement ParseJson(string? json)
    {
        using var document = JsonDocument.Parse(json!);
        return document.RootElement.Clone();
    }

    protected static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(ProblemContentType, response.Content.Headers.ContentType?.MediaType);
        var problem = await ReadJsonAsync(response);
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        return problem;
    }

    protected static void AssertServerTiming(HttpResponseMessage response)
        => Assert.Matches(ServerTimingPattern, response.Headers.GetValues("Server-Timing").Single());

    protected static string[] ErrorKeys(JsonElement problem)
        => problem.GetProperty("errors").EnumerateObject().Select(property => property.Name).ToArray();
}
