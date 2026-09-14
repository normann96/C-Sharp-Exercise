namespace CSharpApp.Infrastructure.Mapping;

public sealed class UpstreamMappingRegister : IRegister
{
    // The names match on both sides, so convention does the mapping; declaring the pairs is what makes them
    // eagerly compiled at startup and checkable as a set.
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<PlatziCategory, Category>();
        config.NewConfig<PlatziProduct, Product>();
    }
}
