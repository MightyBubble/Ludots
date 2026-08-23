using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.Items
{
    public sealed class InventoryEquipmentGrantSyncSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription DirtyActorsQuery = new QueryDescription().WithAll<InventoryEquipmentDirtyTag>();

        private readonly InventoryRuntimeService _inventory;
        private readonly EffectRequestQueue _effectRequests;
        private readonly AbilityDefinitionRegistry _abilityDefinitions;
        private Entity[] _dirtyActors = new Entity[64];
        private readonly List<Entity> _grantItems = new(32);
        private readonly List<Entity> _staleEffects = new(32);
        private readonly List<DesiredPassiveEffect> _desiredEffects = new(32);
        private bool[] _matchedDesiredEffects = Array.Empty<bool>();

        public InventoryEquipmentGrantSyncSystem(
            World world,
            InventoryRuntimeService inventory,
            EffectRequestQueue effectRequests,
            AbilityDefinitionRegistry abilityDefinitions)
            : base(world)
        {
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _effectRequests = effectRequests ?? throw new ArgumentNullException(nameof(effectRequests));
            _abilityDefinitions = abilityDefinitions ?? throw new ArgumentNullException(nameof(abilityDefinitions));
        }

        public override void Update(in float dt)
        {
            int dirtyCount = World.CountEntities(in DirtyActorsQuery);
            if (dirtyCount <= 0)
            {
                return;
            }

            if (_dirtyActors.Length < dirtyCount)
            {
                _dirtyActors = new Entity[dirtyCount * 2];
            }

            World.GetEntities(in DirtyActorsQuery, _dirtyActors);
            for (int i = 0; i < dirtyCount; i++)
            {
                Entity actor = _dirtyActors[i];
                if (!World.IsAlive(actor))
                {
                    continue;
                }

                SyncActor(actor);
            }
        }

        private void SyncActor(Entity actor)
        {
            _grantItems.Clear();
            _desiredEffects.Clear();
            _staleEffects.Clear();

            _inventory.CollectEquippedGrantItems(actor, _grantItems);
            SyncGrantedAbilities(actor, _grantItems);
            BuildDesiredPassiveEffects(_grantItems, _desiredEffects);
            SyncPassiveEffects(actor, _desiredEffects, _staleEffects);

            if (World.Has<InventoryEquipmentDirtyTag>(actor))
            {
                World.Remove<InventoryEquipmentDirtyTag>(actor);
            }
        }

        private void SyncGrantedAbilities(Entity actor, List<Entity> grantItems)
        {
            ItemGrantedSlotBuffer next = default;
            next.ClearAll();
            Span<int> grantedAbilityIds = stackalloc int[ItemGrantedSlotBuffer.CAPACITY];
            int grantedAbilityCount = 0;
            bool hasAny = false;

            for (int i = 0; i < grantItems.Count; i++)
            {
                Entity item = grantItems[i];
                if (!World.IsAlive(item) || !World.Has<ItemInstanceCm>(item))
                {
                    continue;
                }

                ItemInstanceCm instance = World.Get<ItemInstanceCm>(item);
                if (!_inventory.TryGetDefinition(instance.DefinitionId, out ItemDefinition definition))
                {
                    continue;
                }

                for (int grantIndex = 0; grantIndex < definition.AbilityGrants.Length; grantIndex++)
                {
                    ItemAbilityGrant grant = definition.AbilityGrants[grantIndex];
                    if ((uint)grant.SlotIndex >= ItemGrantedSlotBuffer.CAPACITY || grant.AbilityId <= 0)
                    {
                        continue;
                    }

                    next.SetOverride(grant.SlotIndex, grant.AbilityId, item);
                    hasAny = true;
                }
            }

            if (!hasAny)
            {
                if (World.Has<ItemGrantedSlotBuffer>(actor))
                {
                    World.Remove<ItemGrantedSlotBuffer>(actor);
                }

                return;
            }

            for (int slotIndex = 0; slotIndex < ItemGrantedSlotBuffer.CAPACITY; slotIndex++)
            {
                if (next.HasOverride(slotIndex))
                {
                    grantedAbilityIds[grantedAbilityCount++] = next.GetOverride(slotIndex).AbilityId;
                }
            }

            AbilityRuntimeStateInstaller.EnsureForAbilities(
                World,
                actor,
                _abilityDefinitions,
                grantedAbilityIds.Slice(0, grantedAbilityCount),
                $"Equipment ability grants for actor id={actor.Id}");

            if (World.Has<ItemGrantedSlotBuffer>(actor))
            {
                ref ItemGrantedSlotBuffer existing = ref World.Get<ItemGrantedSlotBuffer>(actor);
                if (!existing.ContentEquals(in next))
                {
                    existing = next;
                }
            }
            else
            {
                World.Add(actor, next);
            }
        }

        private void BuildDesiredPassiveEffects(List<Entity> grantItems, List<DesiredPassiveEffect> output)
        {
            for (int i = 0; i < grantItems.Count; i++)
            {
                Entity item = grantItems[i];
                if (!World.IsAlive(item) || !World.Has<ItemInstanceCm>(item))
                {
                    continue;
                }

                ItemInstanceCm instance = World.Get<ItemInstanceCm>(item);
                if (!_inventory.TryGetDefinition(instance.DefinitionId, out ItemDefinition definition))
                {
                    continue;
                }

                for (int effectIndex = 0; effectIndex < definition.EquipEffectTemplateIds.Length; effectIndex++)
                {
                    int templateId = definition.EquipEffectTemplateIds[effectIndex];
                    if (templateId > 0)
                    {
                        output.Add(new DesiredPassiveEffect(templateId, item));
                    }
                }
            }
        }

        private void SyncPassiveEffects(Entity actor, List<DesiredPassiveEffect> desiredEffects, List<Entity> staleEffects)
        {
            if (_matchedDesiredEffects.Length < desiredEffects.Count)
            {
                _matchedDesiredEffects = new bool[desiredEffects.Count * 2];
            }

            Array.Clear(_matchedDesiredEffects, 0, desiredEffects.Count);

            if (World.Has<ActiveEffectContainer>(actor))
            {
                ref ActiveEffectContainer active = ref World.Get<ActiveEffectContainer>(actor);
                for (int i = 0; i < active.Count; i++)
                {
                    Entity effectEntity = active.GetEntity(i);
                    if (!World.IsAlive(effectEntity) ||
                        !World.Has<GameplayEffect>(effectEntity) ||
                        !World.Has<EffectTemplateRef>(effectEntity) ||
                        !World.Has<EffectContext>(effectEntity))
                    {
                        continue;
                    }

                    ref GameplayEffect effect = ref World.Get<GameplayEffect>(effectEntity);
                    ref EffectContext context = ref World.Get<EffectContext>(effectEntity);
                    if (context.Source != actor || context.Target != actor)
                    {
                        continue;
                    }

                    int templateId = World.Get<EffectTemplateRef>(effectEntity).TemplateId;
                    int desiredIndex = FindDesiredEffect(desiredEffects, _matchedDesiredEffects, templateId, context.TargetContext);
                    if (desiredIndex >= 0 && !effect.CancelRequested)
                    {
                        _matchedDesiredEffects[desiredIndex] = true;
                        continue;
                    }

                    if (context.TargetContext == Entity.Null ||
                        !World.IsAlive(context.TargetContext) ||
                        World.Has<ItemInstanceCm>(context.TargetContext))
                    {
                        staleEffects.Add(effectEntity);
                    }
                }
            }

            for (int i = 0; i < staleEffects.Count; i++)
            {
                Entity effectEntity = staleEffects[i];
                if (!World.IsAlive(effectEntity) || !World.Has<GameplayEffect>(effectEntity))
                {
                    continue;
                }

                ref GameplayEffect effect = ref World.Get<GameplayEffect>(effectEntity);
                effect.CancelRequested = true;
                if (effect.AggregatesModifiers && !World.Has<AttributeAggregateDirty>(actor))
                {
                    World.Add(actor, new AttributeAggregateDirty());
                }
            }

            for (int i = 0; i < desiredEffects.Count; i++)
            {
                if (_matchedDesiredEffects[i])
                {
                    continue;
                }

                DesiredPassiveEffect desired = desiredEffects[i];
                _effectRequests.Publish(new EffectRequest
                {
                    Source = actor,
                    Target = actor,
                    TargetContext = desired.Item,
                    TemplateId = desired.TemplateId
                });
            }
        }

        private static int FindDesiredEffect(List<DesiredPassiveEffect> desiredEffects, bool[] matched, int templateId, Entity item)
        {
            for (int i = 0; i < desiredEffects.Count; i++)
            {
                DesiredPassiveEffect desired = desiredEffects[i];
                if (desired.TemplateId != templateId || desired.Item != item)
                {
                    continue;
                }

                if (!matched[i])
                {
                    return i;
                }
            }

            return -1;
        }

        private readonly record struct DesiredPassiveEffect(int TemplateId, Entity Item);
    }
}
