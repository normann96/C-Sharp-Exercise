using CSharpApp.Application.Products;
using CSharpApp.Core.Dtos;
using CSharpApp.Core.Interfaces;
using CSharpApp.Core.Settings;
using CSharpApp.UnitTests.TestDoubles;
using FluentValidation;
using MediatR;
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
    public async Task InvalidRequest_IsRejectedByThePipelineBeforeReachingTheUpstream()
    {
        // Arrange: nothing but the registration itself proves the validation behaviour is in the MediatR pipeline
        var client = new FakePlatziStoreClient();
        var provider = BuildProvider(configureServices: services => services.AddSingleton<IPlatziStoreClient>(client));
        var sender = provider.GetRequiredService<ISender>();

        // Act
        var act = () => sender.Send(new GetProductByIdQuery(0));

        // Assert
        await Assert.ThrowsAsync<ValidationException>(act);
        Assert.Null(client.RequestedProductId);
    }

    [Fact]
    public async Task ValidRequest_ReachesTheUpstreamThroughThePipeline()
    {
        // Arrange
        var client = new FakePlatziStoreClient { Product = new Product { Id = 7 } };
        var provider = BuildProvider(configureServices: services => services.AddSingleton<IPlatziStoreClient>(client));
        var sender = provider.GetRequiredService<ISender>();

        // Act
        var product = await sender.Send(new GetProductByIdQuery(7));

        // Assert
        Assert.Equal(7, client.RequestedProductId);
        Assert.Equal(7, product!.Id);
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
