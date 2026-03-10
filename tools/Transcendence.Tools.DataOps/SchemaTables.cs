using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Transcendence.Data;

namespace Transcendence.Tools.DataOps;

internal static class SchemaTables
{
    private static readonly Lazy<IReadOnlyList<IEntityType>> EntityTypes = new(BuildEntityTypes);

    public static string For<TEntity>()
        where TEntity : class
    {
        return GetTableName(FindEntityType(typeof(TEntity)));
    }

    public static string ForManyToMany<TLeft, TRight>()
        where TLeft : class
        where TRight : class
    {
        var joinEntityType = EntityTypes.Value
            .Where(entityType => entityType.GetTableName() != null)
            .Where(entityType => entityType.GetForeignKeys().Count() == 2)
            .SingleOrDefault(entityType =>
            {
                var principalTypes = entityType.GetForeignKeys()
                    .Select(foreignKey => foreignKey.PrincipalEntityType.ClrType)
                    .ToArray();

                return principalTypes.Contains(typeof(TLeft))
                       && principalTypes.Contains(typeof(TRight));
            });

        if (joinEntityType == null)
        {
            throw new InvalidOperationException(
                $"No implicit join table mapping was found for {typeof(TLeft).FullName} <-> {typeof(TRight).FullName}.");
        }

        return GetTableName(joinEntityType);
    }

    private static IEntityType FindEntityType(Type clrType)
    {
        var entityType = EntityTypes.Value
            .SingleOrDefault(candidate =>
                candidate.ClrType == clrType
                && string.Equals(candidate.Name, clrType.FullName, StringComparison.Ordinal));

        if (entityType == null)
            throw new InvalidOperationException($"No mapped entity type was found for {clrType.FullName}.");

        return entityType;
    }

    private static string GetTableName(IEntityType entityType)
    {
        return entityType.GetTableName()
               ?? throw new InvalidOperationException(
                   $"Entity type {entityType.Name} does not have a relational table mapping.");
    }

    private static IReadOnlyList<IEntityType> BuildEntityTypes()
    {
        using var context = new TranscendenceContext(new DbContextOptionsBuilder<TranscendenceContext>().Options);
        return context.Model.GetEntityTypes().ToArray();
    }
}
