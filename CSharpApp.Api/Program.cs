var builder = WebApplication.CreateBuilder(args);

var logger = new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).CreateLogger();
builder.Logging.ClearProviders().AddSerilog(logger);

builder.Services.AddOpenApi(options => options.AddDocumentTransformer<VersionedPathDocumentTransformer>());
builder.Services.AddApplication();
builder.Services.AddDefaultConfiguration();
builder.Services.AddHttpConfiguration();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddApiVersioning(options =>
{
    // Versions are URL segments; the default reader would also probe the query string on every request (AV0015).
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
});

// Serialize our own responses with the same source-generated context the upstream calls use.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, PlatziJsonContext.Default));

var app = builder.Build();

// Timing sits outermost so its measurement and logged status include error handling; the exception handler
// sits next so every failure below it becomes a problem details response.
app.UseMiddleware<RequestTimingMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var api = app.NewVersionedApi();
api.MapProductEndpoints();
api.MapCategoryEndpoints();

app.MapHealthEndpoints();

app.Run();

// Names the entry point for WebApplicationFactory; the generated class's accessibility is the compiler's choice, not ours.
public partial class Program;
