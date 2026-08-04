namespace FlowFocus.Tests.Builders;

/// <summary>
/// Abstract base builder implementing generic fluent building logic for domain entities with an integer Id.
/// </summary>
/// <typeparam name="TEntity">Entity type being constructed.</typeparam>
/// <typeparam name="TBuilder">Concrete builder type inheriting from this base.</typeparam>
public abstract class EntityBuilder<TEntity, TBuilder>
    where TEntity : class
    where TBuilder : EntityBuilder<TEntity, TBuilder>
{
    protected int Id = 1;

    public TBuilder WithId(int id)
    {
        Id = id;
        return (TBuilder)this;
    }

    public abstract TEntity Build();
}
