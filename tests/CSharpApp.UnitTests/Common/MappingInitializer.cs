using System.Runtime.CompilerServices;
using CSharpApp.Infrastructure.Mapping;
using Mapster;

namespace CSharpApp.UnitTests.Common;

internal static class MappingInitializer
{
    // Adapt() reads the global configuration, so the tests have to set it up the way the composition root does
    // before the first test touches a mapping.
    [ModuleInitializer]
    internal static void Initialize() => TypeAdapterConfig.GlobalSettings.Scan(typeof(UpstreamMappingRegister).Assembly);
}
