using CSharpApp.Core.Dtos;
using CSharpApp.IntegrationTests.Common;
using CSharpApp.IntegrationTests.TestDoubles;

namespace CSharpApp.IntegrationTests.Endpoints;

public sealed class AuthFlowTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    private const int ConcurrentRequests = 10;
    private static readonly TimeSpan HoldTimeout = TimeSpan.FromSeconds(10);

    private static readonly object ValidProduct = new
    {
        title = "Replayed", price = 10.5, description = "d", categoryId = 1, images = new[] { "https://img.example/1.png" },
    };

    [Fact]
    public async Task ColdStart_LogsInWithTheConfiguredCredentials_AndKeepsTheTokenForLater()
    {
        // Arrange: a host of its own, so the first request is a cold start
        using var coldHost = Fixture.WithWebHostBuilder(_ => { });
        var client = coldHost.CreateClient();
        var logins = Fake.LoginCount;

        // Act
        var first = await client.GetAsync(ProductsRoute);
        var second = await client.GetAsync($"{CategoriesRoute}/1");

        // Assert
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(logins + 1, Fake.LoginCount);
        var login = Fake.LastLogin;
        Assert.Equal(FakePlatziHandler.ApiPrefix + "/auth/login", login.PathAndQuery);
        Assert.Null(login.Authorization);
        var credentials = ParseJson(login.Body);
        Assert.Equal(ApiFixture.Username, credentials.GetProperty("email").GetString());
        Assert.Equal(ApiFixture.Password, credentials.GetProperty("password").GetString());
    }

    [Fact]
    public async Task ColdStart_ConcurrentRequests_ShareOneLogin_WhileItIsStillPending()
    {
        // Arrange: a cold host, and a login the fake keeps open until every request has had time to reach the gate
        using var coldHost = Fixture.WithWebHostBuilder(_ => { });
        var client = coldHost.CreateClient();
        var logins = Fake.LoginCount;
        var hold = Fake.HoldLogins();

        // Act
        var requests = Enumerable.Range(0, ConcurrentRequests).Select(_ => client.GetAsync(ProductsRoute)).ToArray();
        await hold.Started.WaitAsync(HoldTimeout);
        await Task.Delay(100); // without single flight the other nine would have started their own logins by now
        var loginsWhilePending = Fake.LoginCount;
        hold.Release();
        var responses = await Task.WhenAll(requests);

        // Assert
        Assert.Equal(logins + 1, loginsWhilePending);
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(logins + 1, Fake.LoginCount);
    }

    [Fact]
    public async Task UpstreamRejectsTheToken_TheCallIsHealedByOneReLogin_WithTheFreshToken()
    {
        // Arrange
        await Client.GetAsync(ProductsRoute); // warms the token cache
        var logins = Fake.LoginCount;
        var staleToken = Fake.LastStoreRequest.Authorization;
        Fake.FailNextStoreCalls(1, HttpStatusCode.Unauthorized);

        // Act
        var response = await Client.GetAsync(ProductsRoute);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(logins + 1, Fake.LoginCount);
        var requests = Fake.StoreRequests;
        Assert.Equal(staleToken, requests[^2].Authorization);
        Assert.NotEqual(staleToken, requests[^1].Authorization);
        Assert.StartsWith("Bearer ", requests[^1].Authorization);
    }

    [Fact]
    public async Task UpstreamRejectsTheToken_OnAWrite_TheBodyIsReplayedIntact()
    {
        // Arrange: the handler must clone a JsonContent body before the first send consumes it
        await Client.GetAsync(ProductsRoute);
        var logins = Fake.LoginCount;
        Fake.FailNextStoreCalls(1, HttpStatusCode.Unauthorized);

        // Act
        var response = await Client.PostAsJsonAsync(ProductsRoute, ValidProduct);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(logins + 1, Fake.LoginCount);
        var requests = Fake.StoreRequests;
        Assert.Equal(HttpMethod.Post, requests[^1].Method);
        Assert.Equal(requests[^2].Body, requests[^1].Body);
        Assert.Equal("Replayed", ParseJson(requests[^1].Body).GetProperty("title").GetString());
        var created = await response.Content.ReadFromJsonAsync<Product>();
        Assert.Equal("Replayed", created!.Title);
    }

    [Fact]
    public async Task ReLoginFails_Returns502_WithoutTheCredentialsOrTheUpstreamAnswer()
    {
        // Arrange
        await Client.GetAsync(ProductsRoute);
        Fake.FailNextStoreCalls(1, HttpStatusCode.Unauthorized);
        Fake.FailLogin = true;

        // Act
        var response = await Client.GetAsync(ProductsRoute);

        // Assert
        var problem = await AssertProblemAsync(response, HttpStatusCode.BadGateway);
        Assert.Equal("The upstream service call failed.", problem.GetProperty("title").GetString());
        Assert.False(problem.TryGetProperty("detail", out _));
        var body = problem.GetRawText();
        Assert.DoesNotContain(ApiFixture.Password, body);
        Assert.DoesNotContain(ApiFixture.Username, body);
        Assert.DoesNotContain(FakePlatziHandler.RejectedLoginBody, body);
    }
}
