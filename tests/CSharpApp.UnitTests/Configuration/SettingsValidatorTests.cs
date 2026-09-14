using CSharpApp.Core.Settings;
using CSharpApp.Infrastructure.Configuration;

namespace CSharpApp.UnitTests.Configuration;

public class RestApiSettingsValidatorTests
{
    private const string Secret = "s3cret-value";
    private readonly RestApiSettingsValidator _validator = new();

    private static RestApiSettings ValidSettings() => new()
    {
        BaseUrl = "https://api.escuelajs.co/api/v1/", Products = "products", Categories = "/categories",
        Auth = "/auth/login", Username = "john@mail.com", Password = Secret
    };

    [Fact]
    public void ValidSettings_Succeed()
    {
        // Arrange
        var settings = ValidSettings();

        // Act
        var result = _validator.Validate(null, settings);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("https://")]
    [InlineData("ftp://example.com/api/")]
    [InlineData("api.escuelajs.co/api/v1/")]
    public void InvalidBaseUrl_FailsNamingTheField(string? baseUrl)
    {
        // Arrange
        var settings = ValidSettings();
        settings.BaseUrl = baseUrl;

        // Act
        var result = _validator.Validate(null, settings);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("BaseUrl", result.FailureMessage);
    }

    [Theory]
    [InlineData(nameof(RestApiSettings.Products))]
    [InlineData(nameof(RestApiSettings.Categories))]
    [InlineData(nameof(RestApiSettings.Auth))]
    [InlineData(nameof(RestApiSettings.Username))]
    [InlineData(nameof(RestApiSettings.Password))]
    public void MissingField_FailsNamingTheField(string property)
    {
        // Arrange
        var settings = ValidSettings();
        typeof(RestApiSettings).GetProperty(property)!.SetValue(settings, null);

        // Act
        var result = _validator.Validate(null, settings);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains(property, result.FailureMessage);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("https://evil.example/products")]
    [InlineData("products?x=1")]
    public void MalformedResourcePath_FailsNamingTheField(string path)
    {
        // Arrange
        var settings = ValidSettings();
        settings.Categories = path;

        // Act
        var result = _validator.Validate(null, settings);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("Categories", result.FailureMessage);
    }

    [Theory]
    [InlineData("products")]
    [InlineData("/categories")]
    [InlineData("/auth/login")]
    [InlineData("v2/items-2.0/")]
    public void WellFormedResourcePath_Passes(string path)
    {
        // Arrange
        var settings = ValidSettings();
        settings.Products = path;

        // Act
        var result = _validator.Validate(null, settings);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void FailureMessages_NeverContainTheSecret()
    {
        // Arrange
        var settings = ValidSettings();
        settings.BaseUrl = "not-a-url";
        settings.Username = null;

        // Act
        var result = _validator.Validate(null, settings);

        // Assert
        Assert.True(result.Failed);
        Assert.DoesNotContain(Secret, result.FailureMessage);
    }
}

public class PerformanceLoggingSettingsValidatorTests
{
    private readonly PerformanceLoggingSettingsValidator _validator = new();

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    [InlineData(60_000)]
    public void ThresholdWithinRange_Succeeds(int thresholdMs)
    {
        // Arrange
        var settings = new PerformanceLoggingSettings { SlowRequestThresholdMs = thresholdMs };

        // Act
        var result = _validator.Validate(null, settings);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(60_001)]
    public void ThresholdOutOfRange_FailsNamingTheField(int thresholdMs)
    {
        // Arrange
        var settings = new PerformanceLoggingSettings { SlowRequestThresholdMs = thresholdMs };

        // Act
        var result = _validator.Validate(null, settings);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("SlowRequestThresholdMs", result.FailureMessage);
    }
}

public class HttpClientSettingsValidatorTests
{
    private readonly HttpClientSettingsValidator _validator = new();

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(1440, 10, 10_000)]
    [InlineData(10, 2, 100)]
    public void ValuesWithinRange_Succeed(int lifeTime, int retryCount, int sleepDuration)
    {
        // Arrange
        var settings = new HttpClientSettings { LifeTime = lifeTime, RetryCount = retryCount, SleepDuration = sleepDuration };

        // Act
        var result = _validator.Validate(null, settings);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(0, 2, 100, "LifeTime")]
    [InlineData(1441, 2, 100, "LifeTime")]
    [InlineData(10, 11, 100, "RetryCount")]
    [InlineData(10, -1, 100, "RetryCount")]
    [InlineData(10, 2, 10_001, "SleepDuration")]
    public void ValuesOutOfRange_FailNamingTheField(int lifeTime, int retryCount, int sleepDuration, string field)
    {
        // Arrange
        var settings = new HttpClientSettings { LifeTime = lifeTime, RetryCount = retryCount, SleepDuration = sleepDuration };

        // Act
        var result = _validator.Validate(null, settings);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains(field, result.FailureMessage);
    }
}

public class RateLimitingSettingsValidatorTests
{
    private readonly RateLimitingSettingsValidator _validator = new();

    private static RateLimitingSettings ValidSettings() => new() { PermitLimit = 100, WindowSeconds = 60 };

    [Fact]
    public void ValidSettings_Succeed()
    {
        // Arrange
        var settings = ValidSettings();

        // Act
        var result = _validator.Validate(null, settings);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_001)]
    public void PermitLimitOutOfRange_FailsNamingTheField(int permitLimit)
    {
        // Arrange: an allowance of zero would refuse everyone, and an unbounded one would not be a limit
        var settings = ValidSettings();
        settings.PermitLimit = permitLimit;

        // Act
        var result = _validator.Validate(null, settings);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(nameof(RateLimitingSettings.PermitLimit), result.FailureMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3601)]
    public void WindowOutOfRange_FailsNamingTheField(int windowSeconds)
    {
        // Arrange
        var settings = ValidSettings();
        settings.WindowSeconds = windowSeconds;

        // Act
        var result = _validator.Validate(null, settings);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(nameof(RateLimitingSettings.WindowSeconds), result.FailureMessage);
    }
}
