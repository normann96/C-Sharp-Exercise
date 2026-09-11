namespace CSharpApp.Infrastructure.Configuration;

// Bodies are source-generated from the DataAnnotations on the settings classes.
[OptionsValidator]
public sealed partial class RestApiSettingsValidator : IValidateOptions<RestApiSettings>;

[OptionsValidator]
public sealed partial class HttpClientSettingsValidator : IValidateOptions<HttpClientSettings>;

[OptionsValidator]
public sealed partial class PerformanceLoggingSettingsValidator : IValidateOptions<PerformanceLoggingSettings>;
