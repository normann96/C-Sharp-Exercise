var builder = WebApplication.CreateBuilder(args);

var logger = new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).CreateLogger();
builder.Logging.ClearProviders().AddSerilog(logger);

builder.Services.AddOpenApi(options => options.AddDocumentTransformer<VersionedPathDocumentTransformer>());
builder.Services.AddApplication();
builder.Services.AddDefaultConfiguration();
builder.Services.AddHttpConfiguration();
builder.Services.AddProblemDetails();
builder.Services.AddApiVersioning(options =>
{
    // Versions are URL segments; the default reader would also probe the query string on every request (AV0015).
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
});

// Serialize our own responses with the same source-generated context the upstream calls use.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, PlatziJsonContext.Default));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var api = app.NewVersionedApi();
api.MapProductEndpoints();

app.Run();
