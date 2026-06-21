using System.Reflection;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Input.Selection;
using Ludots.Core.Persistence;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using NUnit.Framework;
using CoreComponentRegistry = Ludots.Core.Config.ComponentRegistry;

namespace Ludots.Tests.Persistence;

[TestFixture]
public sealed class EntityReferenceCoverageGuardTests
{
    private static readonly IReadOnlySet<Type> RuntimePersistedComponentTypes = new HashSet<Type>
    {
        typeof(ChildrenBuffer),
        typeof(ActiveEffectContainer),
        typeof(GrantedSlotBuffer),
        typeof(AbilityFormSlotBuffer),
        typeof(AbilityExecInstance),
        typeof(AbilityTaskInstance),
        typeof(EffectContext),
        typeof(ChildOf),
        typeof(DisplacementState),
        typeof(ProjectileState),
        typeof(OrderContinuationBuffer),
        typeof(ScopeRefBuffer),
        typeof(UtilityAiState),
        typeof(UtilityAiDecisionTrace),
        typeof(UtilityAiCombatMemory),
        typeof(SelectionContainerOwner),
        typeof(SelectionMemberContainer),
        typeof(SelectionMemberTarget),
        typeof(SelectionViewBindingViewer),
        typeof(SelectionViewBindingContainer),
        typeof(SelectionLeaseContainer),
        typeof(ItemLocationCm),
        typeof(ItemMountedContainerCm),
        typeof(ItemGrantedSlotBuffer),
        typeof(PresentationOwnerHasPerformerPayload),
        typeof(PerformerState),
        typeof(PerformerParent),
        typeof(PerformerChildren)
    };

    private static readonly IReadOnlySet<Type> FlattenedEntityReferenceComponents = new HashSet<Type>
    {
        typeof(BlackboardEntityBuffer),
        typeof(ChildrenBuffer),
        typeof(ActiveEffectContainer),
        typeof(AbilityStateBuffer),
        typeof(GrantedSlotBuffer),
        typeof(AbilityFormSlotBuffer),
        typeof(AbilityExecInstance),
        typeof(AbilityTaskInstance),
        typeof(ProjectileState),
        typeof(ItemGrantedSlotBuffer),
        typeof(ScopeRefBuffer),
        typeof(PerformerChildren)
    };

    [Test]
    public void PersistedEntityReferenceComponentsAreAuditedAndNormalized()
    {
        IReadOnlySet<Type> formatterTypes = LudotsCorePersistenceFormatters.GetFormatterComponentTypes();
        IReadOnlySet<Type> validatorTypes = SaveEntityReferenceValidator.GetCoveredComponentTypes();
        IReadOnlySet<Type> normalizerTypes = SaveEntityWorldIdNormalizer.GetCoveredComponentTypes();

        string[] runtimeTypesMissingFormatter = RuntimePersistedComponentTypes
            .Where(type => !formatterTypes.Contains(type))
            .Select(DisplayName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Type[] entityReferenceComponents = GetPersistedComponentTypes(formatterTypes)
            .Where(type => ContainsEntityField(type) || FlattenedEntityReferenceComponents.Contains(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        string[] missingValidator = entityReferenceComponents
            .Where(type => !validatorTypes.Contains(type))
            .Select(DisplayName)
            .ToArray();
        string[] missingNormalizer = entityReferenceComponents
            .Where(type => !normalizerTypes.Contains(type))
            .Select(DisplayName)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(
                runtimeTypesMissingFormatter,
                Is.Empty,
                "Runtime-created persisted component types must have Ludots persistence formatters.");
            Assert.That(
                missingValidator,
                Is.Empty,
                "Every persisted component carrying Entity references must be covered by SaveEntityReferenceValidator.");
            Assert.That(
                missingNormalizer,
                Is.Empty,
                "Every persisted component carrying Entity references must be covered by SaveEntityWorldIdNormalizer.");
        });
    }

    private static IReadOnlySet<Type> GetPersistedComponentTypes(IReadOnlySet<Type> formatterTypes)
    {
        return CoreComponentRegistry.GetRegisteredComponentTypes()
            .Values
            .Select(componentType => componentType.Type)
            .Concat(RuntimePersistedComponentTypes)
            .Where(type => formatterTypes.Contains(type))
            .ToHashSet();
    }

    private static bool ContainsEntityField(Type type)
    {
        return ContainsEntityField(type, new HashSet<Type>());
    }

    private static bool ContainsEntityField(Type type, HashSet<Type> visiting)
    {
        if (type == typeof(Entity))
        {
            return true;
        }

        if (!type.IsValueType || type.IsPrimitive || type.IsEnum || !visiting.Add(type))
        {
            return false;
        }

        FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < fields.Length; i++)
        {
            Type fieldType = fields[i].FieldType;
            if (fieldType == typeof(Entity) || ContainsEntityField(fieldType, visiting))
            {
                return true;
            }
        }

        return false;
    }

    private static string DisplayName(Type type)
    {
        return type.FullName ?? type.Name;
    }
}
