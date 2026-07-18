using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Config;

/// <summary>
/// Single source of truth for runtime components implied by authored entity state.
/// Scalar authoring applies the plan after components are parsed; batch authoring
/// materializes the same plan directly into its archetype signature.
/// </summary>
public readonly struct EntityRuntimeStatePlan
{
    private EntityRuntimeStatePlan(
        bool hasGameplayTagContainer,
        bool hasTagCountContainer,
        bool hasDirtyFlags,
        bool hasTimedTagBuffer,
        bool hasOrderRuntimeState)
    {
        HasGameplayTagContainer = hasGameplayTagContainer;
        HasTagCountContainer = hasTagCountContainer;
        HasDirtyFlags = hasDirtyFlags;
        HasTimedTagBuffer = hasTimedTagBuffer;
        HasOrderRuntimeState = hasOrderRuntimeState;
    }

    public bool HasGameplayTagContainer { get; }
    public bool HasTagCountContainer { get; }
    public bool HasDirtyFlags { get; }
    public bool HasTimedTagBuffer { get; }
    public bool HasOrderRuntimeState { get; }

    public static EntityRuntimeStatePlan FromAuthoredComponents(
        IReadOnlyDictionary<string, JsonNode> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        bool hasAbilityTagGrantReceiver = components.ContainsKey("AbilityTagGrantReceiver");
        bool hasGameplayTagContainer =
            hasAbilityTagGrantReceiver || components.ContainsKey("GameplayTagContainer");
        bool hasTagCountContainer =
            hasGameplayTagContainer || components.ContainsKey("TagCountContainer");
        bool hasDirtyFlags =
            hasGameplayTagContainer ||
            components.ContainsKey("AttributeBuffer") ||
            components.ContainsKey("DirtyFlags");
        bool hasTimedTagBuffer =
            hasAbilityTagGrantReceiver || components.ContainsKey("TimedTagBuffer");

        return new EntityRuntimeStatePlan(
            hasGameplayTagContainer,
            hasTagCountContainer,
            hasDirtyFlags,
            hasTimedTagBuffer,
            components.ContainsKey("OrderBuffer"));
    }

    public static EntityRuntimeStatePlan FromEntity(World world, Entity entity)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!world.IsAlive(entity))
        {
            throw new InvalidOperationException($"ENTITY.RUNTIME_STATE.ERR.DeadEntity: entity={entity.Id}.");
        }

        bool hasAbilityTagGrantReceiver = world.Has<AbilityTagGrantReceiver>(entity);
        bool hasGameplayTagContainer =
            hasAbilityTagGrantReceiver || world.Has<GameplayTagContainer>(entity);
        bool hasTagCountContainer =
            hasGameplayTagContainer || world.Has<TagCountContainer>(entity);
        bool hasDirtyFlags =
            hasGameplayTagContainer ||
            world.Has<AttributeBuffer>(entity) ||
            world.Has<DirtyFlags>(entity);
        bool hasTimedTagBuffer =
            hasAbilityTagGrantReceiver || world.Has<TimedTagBuffer>(entity);

        return new EntityRuntimeStatePlan(
            hasGameplayTagContainer,
            hasTagCountContainer,
            hasDirtyFlags,
            hasTimedTagBuffer,
            world.Has<OrderBuffer>(entity));
    }

    public void EnsureInstalled(World world, Entity entity)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!world.IsAlive(entity))
        {
            throw new InvalidOperationException($"ENTITY.RUNTIME_STATE.ERR.DeadEntity: entity={entity.Id}.");
        }

        if (HasOrderRuntimeState)
        {
            OrderBlackboardStateInstaller.EnsureInstalled(world, entity);
            OrderContinuationStateInstaller.EnsureInstalled(world, entity);
        }

        if (HasGameplayTagContainer)
        {
            TagStateInstaller.EnsureInstalled(world, entity);
        }
        else
        {
            if (HasTagCountContainer && !world.Has<TagCountContainer>(entity))
            {
                world.Add(entity, new TagCountContainer());
            }
            if (HasDirtyFlags && !world.Has<DirtyFlags>(entity))
            {
                world.Add(entity, new DirtyFlags());
            }
        }

        if (HasTimedTagBuffer && !world.Has<TimedTagBuffer>(entity))
        {
            world.Add(entity, new TimedTagBuffer());
        }
    }

    public static void EnsureInstalledForAuthoredEntity(
        World world,
        Entity entity,
        ComponentAuthoringContext authoringContext,
        string entityContext)
    {
        ArgumentNullException.ThrowIfNull(authoringContext);
        EntityRuntimeStatePlan runtimeStatePlan = FromEntity(world, entity);
        runtimeStatePlan.EnsureInstalled(world, entity);

        if (!world.Has<AbilityStateBuffer>(entity))
        {
            return;
        }

        AbilityFormSetRegistry? formSets = world.Has<AbilityFormSetRef>(entity)
            ? authoringContext.Require<AbilityFormSetRegistry>(ComponentAuthoringServiceKeys.AbilityFormSetRegistry)
            : null;
        AbilityRuntimeStateInstaller.EnsureForAuthoredAbilities(
            world,
            entity,
            authoringContext.Require<AbilityDefinitionRegistry>(ComponentAuthoringServiceKeys.AbilityDefinitionRegistry),
            formSets,
            entityContext);
    }
}
