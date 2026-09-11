using CSharpApp.Core.Settings;
using CSharpApp.UnitTests.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CSharpApp.UnitTests.Configuration;

public class DefaultConfigurationTests : ServiceProviderTestBase
{
    [Fact]
    public void BindsSectionsNamedAfterTheSettingsTypes()
    {
        // Arrange
        var provider = BuildProvider();

        // Act
        var restApi = provider.GetRequiredService<IOptions<RestApiSettings>>().Value;
        var httpClient = provider.GetRequiredService<IOptions<HttpClientSettings>>().Value;
        var validate = () => provider.GetRequiredService<IStartupValidator>().Validate();

        // Assert
        Assert.Equal("/categories", restApi.Categories);
        Assert.Equal(2, httpClient.RetryCount);
        validate(); // does not throw
    }

    [Fact]
    public void StartupValidation_RejectsInvalidValue()
    {
        // Arrange
        var configuration = ValidConfiguration();
        configuration["RestApiSettings:BaseUrl"] = "https://";
        var provider = BuildProvider(configuration);

        // Act
        var exception = Assert.ThrowsAny<Exception>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        // Assert
        Assert.Contains("BaseUrl", exception.ToString());
    }

    [Fact]
    public void StartupValidation_RejectsMisspelledKey()
    {
        // Arrange
        var configuration = ValidConfiguration();
        configuration["HttpClientSettings:RetryCoutn"] = "3";
        var provider = BuildProvider(configuration);

        // Act
        var exception = Assert.ThrowsAny<Exception>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        // Assert
        Assert.Contains("RetryCoutn", exception.ToString());
    }
}
