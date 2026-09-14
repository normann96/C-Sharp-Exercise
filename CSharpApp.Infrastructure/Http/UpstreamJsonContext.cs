using CSharpApp.Infrastructure.Http.Contracts;

namespace CSharpApp.Infrastructure.Http;

/// <summary>Reading the upstream's wire format. Separate from the context that writes this service's own responses,
/// so the two contracts can move independently.</summary>
[JsonSerializable(typeof(PlatziProduct))]
[JsonSerializable(typeof(List<PlatziProduct>))]
[JsonSerializable(typeof(PlatziCategory))]
[JsonSerializable(typeof(List<PlatziCategory>))]
public sealed partial class UpstreamJsonContext : JsonSerializerContext;
