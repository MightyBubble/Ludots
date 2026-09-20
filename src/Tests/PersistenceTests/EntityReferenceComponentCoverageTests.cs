using System.Reflection;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Persistence;

/// <summary>
/// Entity-reference coverage guard: every component type that appears on save-included entities in
/// a default engine world and carries Arch.Core.Entity references (direct fields, entity-keyed
/// collections, or parallel *WorldIds buffers) must be registered in both
/// SaveEntityReferenceValidator and SaveEntityWorldIdNormalizer. A type failing here was forgotten
/// at registration time — add it to both handlers and to the registered list below.
/// </summary>
public sealed class EntityReferenceComponentCoverageTests
{
    private static readonly string[] RegisteredHandlerTargets =
    {
        "Ludots.Core.Gameplay.GAS.Components.BlackboardEntityBuffer",
        "Ludots.Core.Gameplay.GAS.Components.ChildrenBuffer",
        "Ludots.Core.Gameplay.GAS.Components.ActiveEffectContainer",
        "Ludots.Core.Gameplay.GAS.Components.AbilityStateBuffer",
        "Ludots.Core.Gameplay.Teams.TeamEntityRef",
        "Ludots.Core.Gameplay.Activities.ActivityInstanceCm",
        "Ludots.Core.Gameplay.Tasks.TaskInstanceCm",
        "Ludots.Core.Gameplay.Relationships.RelationshipInstanceCm",
        "Ludots.Core.Gameplay.Relationships.RelationshipEdgeSet",
        "Arch.Relationships.InRelationship",
        "Ludots.Core.Gameplay.GAS.Components.OrderBuffer",
        "Ludots.Core.Gameplay.GAS.Components.OrderContinuationBuffer",
    };

    [Test]
    public void ComponentsCarryingEntityReferencesOnSaveIncludedEntitiesAreRegistered()
    {
        using GameEngine engine = CreateInitializedEngine();
        World world = engine.World;
        var policy = SaveEntityInclusionPolicy.Default;
        var carrying = new HashSet<string>();

        world.Query(in QueryDescription.Null, entity =>
        {
            if (!policy.ShouldInclude(world, entity))
            {
                return;
            }

            foreach (ComponentType componentType in world.GetSignature(entity).Components)
            {
                Type type = componentType.Type;
                if (CarriesEntityReference(type, 0))
                {
                    carrying.Add(GetRegistrationTargetName(type));
                }
            }
        });

        List<string> missing = carrying
            .Where(name => Array.IndexOf(RegisteredHandlerTargets, name) < 0)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        Assert.That(
            missing,
            Is.Empty,
            "Components on save-included entities carrying Entity references are not covered by SaveEntityReferenceValidator/SaveEntityWorldIdNormalizer: " +
            string.Join(", ", missing));
    }

    private static GameEngine CreateInitializedEngine()
    {
        string? dir = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            string gitPath = Path.Combine(dir, ".git");
            if ((Directory.Exists(gitPath) || File.Exists(gitPath)) &&
                Directory.Exists(Path.Combine(dir, "src")) &&
                Directory.Exists(Path.Combine(dir, "mods")))
            {
                break;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(dir!, new[] { "LudotsCoreMod" }),
            Path.Combine(dir!, "assets"));
        engine.LoadStartupMap();
        return engine;
    }

    private static string GetRegistrationTargetName(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition().FullName == "Arch.Relationships.Relationship`1")
        {
            return type.GetGenericArguments()[0].FullName!;
        }

        return type.FullName!;
    }

    private static bool CarriesEntityReference(Type type, int depth)
    {
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || depth > 4)
        {
            return false;
        }

        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (field.FieldType == typeof(Entity) ||
                field.Name.EndsWith("WorldIds", StringComparison.Ordinal))
            {
                return true;
            }

            if (field.FieldType.IsGenericType &&
                field.FieldType.GetGenericArguments().Contains(typeof(Entity)))
            {
                return true;
            }

            if (field.FieldType.IsValueType && !field.FieldType.IsPrimitive && !field.FieldType.IsEnum &&
                CarriesEntityReference(field.FieldType, depth + 1))
            {
                return true;
            }
        }

        return false;
    }
}
