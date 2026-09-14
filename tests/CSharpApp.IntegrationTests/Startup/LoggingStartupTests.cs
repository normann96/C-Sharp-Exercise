using CSharpApp.IntegrationTests.Common;
using Microsoft.AspNetCore.Hosting;

namespace CSharpApp.IntegrationTests.Startup;

public sealed class LoggingStartupTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public void EnvironmentWithoutItsOwnSettingsFile_RefusesToStart()
    {
        // Arrange: each environment declares its own console sink, so a missing file would start a silent service
        using var host = Fixture.WithWebHostBuilder(builder => builder.UseEnvironment("NoSuchEnvironment"));

        // Act
        var failure = Record.Exception(host.CreateClient);

        // Assert
        Assert.NotNull(failure);
        Assert.Contains("NoSuchEnvironment", failure.ToString());
    }
}
