using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Spatial;

namespace Ludots.Core.Input.Orders
{
    public enum AbilityAimInputSlot : byte
    {
        Target = 0,
        VectorOrigin = 1,
        VectorDirection = 2,
    }

    public readonly struct AbilityAimInputState
    {
        public AbilityAimInputState(
            AbilityAimInputSlot slot,
            bool hasCursorWorldCm,
            Vector3 cursorWorldCm,
            bool hasOriginWorldCm,
            Vector3 originWorldCm,
            Entity hoveredEntity)
            : this(slot, hasCursorWorldCm, cursorWorldCm, hasOriginWorldCm, originWorldCm, hoveredEntity, Entity.Null)
        {
        }

        public AbilityAimInputState(
            AbilityAimInputSlot slot,
            bool hasCursorWorldCm,
            Vector3 cursorWorldCm,
            bool hasOriginWorldCm,
            Vector3 originWorldCm,
            Entity hoveredEntity,
            Entity viewerEntity)
        {
            Slot = slot;
            HasCursorWorldCm = hasCursorWorldCm;
            CursorWorldCm = cursorWorldCm;
            HasOriginWorldCm = hasOriginWorldCm;
            OriginWorldCm = originWorldCm;
            HoveredEntity = hoveredEntity;
            ViewerEntity = viewerEntity;
        }

        public AbilityAimInputSlot Slot { get; }
        public bool HasCursorWorldCm { get; }
        public Vector3 CursorWorldCm { get; }
        public bool HasOriginWorldCm { get; }
        public Vector3 OriginWorldCm { get; }
        public Entity HoveredEntity { get; }
        public Entity ViewerEntity { get; }
    }

    public static class AbilityAimPresentationEventKeys
    {
        public const string Range = "ability.aim.range";
        public const string AreaCircle = "ability.aim.area.circle";
        public const string AreaRing = "ability.aim.area.ring";
        public const string AreaCone = "ability.aim.area.cone";
        public const string AreaLine = "ability.aim.area.line";
        public const string AreaRectangle = "ability.aim.area.rectangle";
        public const string Preview = "ability.aim.preview";
    }

    public sealed class AbilityAimPresentationRuntime
    {
        private const float OverlayY = 0.03f;
        private const int AreaScopeOffset = 44000;
        private const int RangeScopeOffset = 44001;
        private const int PreviewScopeOffset = 44002;
        private const int MaxPreviewTargets = 256;
        private const int PrimaryAimTargetRoleId = 1;

        private readonly World _world;
        private readonly AbilityDefinitionRegistry _abilities;
        private readonly EffectTemplateRegistry _effects;
        private readonly EntityCollectionStore _collections;
        private readonly ISpatialQueryService _spatialQueries;
        private readonly PresentationEventStream _events;
        private readonly GameSession? _session;
        private readonly GraphProgramRegistry? _graphPrograms;
        private readonly GasGraphRuntimeApi? _graphApi;
        private readonly Entity[] _candidateBuffer = new Entity[MaxPreviewTargets];
        private readonly int[] _rowRoleIdBuffer = new int[MaxPreviewTargets];
        private readonly EntityCollectionRowFlags[] _rowFlagBuffer = new EntityCollectionRowFlags[MaxPreviewTargets];
        private readonly List<FanOutCommand> _fanOutCommands = new(MaxPreviewTargets);
        private readonly RootBudgetTable _budget = new(MaxPreviewTargets);
        private readonly Dictionary<int, int> _activeSemanticPreviewKeyIdsByActor = new();
        private readonly Dictionary<int, int> _activeAimSessionKeysByActor = new();
        private readonly Dictionary<int, byte> _activeInputSlotsByActor = new();

        private readonly struct AimImpactDescriptor
        {
            public AimImpactDescriptor(
                TargetQueryDescriptor query,
                TargetFilterDescriptor filter,
                TargetDispatchDescriptor dispatch,
                int semanticEventKeyId = 0)
            {
                Query = query;
                Filter = filter;
                Dispatch = dispatch;
                SemanticEventKeyId = semanticEventKeyId;
            }

            public TargetQueryDescriptor Query { get; }
            public TargetFilterDescriptor Filter { get; }
            public TargetDispatchDescriptor Dispatch { get; }
            public int SemanticEventKeyId { get; }
            public bool HasTargetResolver => Query.Kind != TargetResolverKind.None;
            public bool HasSemanticEventKey => SemanticEventKeyId > 0;
        }

        public AbilityAimPresentationRuntime(
            World world,
            AbilityDefinitionRegistry abilities,
            EffectTemplateRegistry effects,
            EntityCollectionStore collections,
            ISpatialQueryService spatialQueries,
            PresentationEventStream events,
            GameSession? session = null,
            GraphProgramRegistry? graphPrograms = null,
            GasGraphRuntimeApi? graphApi = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _abilities = abilities ?? throw new ArgumentNullException(nameof(abilities));
            _effects = effects ?? throw new ArgumentNullException(nameof(effects));
            _collections = collections ?? throw new ArgumentNullException(nameof(collections));
            _spatialQueries = spatialQueries ?? throw new ArgumentNullException(nameof(spatialQueries));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _session = session;
            _graphPrograms = graphPrograms;
            _graphApi = graphApi;
        }

        public void UpdateAiming(Entity actor, InputOrderMapping mapping, in AbilityAimInputState input)
        {
            if (!TryResolveAbility(actor, mapping, out AbilityDefinition definition) ||
                !definition.HasTargeting ||
                !_effects.TryGet(definition.Targeting.ImpactEffectTemplateId, out EffectTemplateData effect) ||
                !TryResolveActorWorld(actor, out Vector3 actorWorldCm))
            {
                Clear(actor);
                return;
            }

            Vector3 aimWorldCm = ResolveAimWorldCm(actorWorldCm, input);
            Vector3 originWorldCm = ResolveOriginWorldCm(actorWorldCm, input);
            bool valid = ClampTargetToRange(originWorldCm, ref aimWorldCm, definition.Targeting.CastRangeCm);
            EffectConfigParams previewParams = BuildPreviewParams(originWorldCm, aimWorldCm);
            AimImpactDescriptor impact = ResolveAimImpact(in effect);
            Entity previewTarget = input.HoveredEntity != Entity.Null ? input.HoveredEntity : actor;
            Entity viewer = ResolveViewer(actor, in input);
            int abilityId = ResolveAbilityId(actor, mapping);
            int actionKeyId = ResolveActionKeyId(mapping.ActionId);
            byte slotIndex = ResolveInputSlotIndex(input.Slot);
            byte slotCount = ResolveInputSlotCount(input.Slot);
            int primaryEventKeyId = impact.HasSemanticEventKey
                ? impact.SemanticEventKeyId
                : ResolveEventKeyId(AbilityAimPresentationEventKeys.Preview);
            PublishLifecycleEvents(
                actor,
                viewer,
                input.HoveredEntity,
                mapping.SelectionType,
                abilityId,
                actionKeyId,
                primaryEventKeyId,
                impact.Query,
                definition.Targeting.CastRangeCm > 0f,
                originWorldCm,
                aimWorldCm,
                slotIndex);
            WriteAimSessionState(
                actor,
                viewer,
                input.HoveredEntity,
                abilityId,
                definition.Targeting.ImpactEffectTemplateId,
                actionKeyId,
                primaryEventKeyId,
                slotIndex,
                slotCount,
                originWorldCm,
                aimWorldCm,
                valid);
            PublishAimHoverCollection(actor, input.HoveredEntity);
            PublishAffectedCollection(actor, previewTarget, in impact, in previewParams, aimWorldCm);
            PublishAimEvents(actor, viewer, input.HoveredEntity, mapping.SelectionType, definition, in impact, originWorldCm, aimWorldCm, valid);
        }

        public void Clear(Entity actor)
        {
            if (actor == Entity.Null)
            {
                return;
            }

            Entity viewer = ResolveActiveViewer(actor);
            _collections.Remove(actor, EntityCollectionKeys.AbilityAimAffected);
            _collections.Remove(actor, EntityCollectionKeys.AbilityAimHover);
            ClearAimSessionState(actor);
            PublishEnded(actor, viewer, AbilityAimPresentationEventKeys.Range, RangeScopeOffset);
            PublishEnded(actor, viewer, AbilityAimPresentationEventKeys.AreaCircle, AreaScopeOffset);
            PublishEnded(actor, viewer, AbilityAimPresentationEventKeys.AreaRing, AreaScopeOffset);
            PublishEnded(actor, viewer, AbilityAimPresentationEventKeys.AreaCone, AreaScopeOffset);
            PublishEnded(actor, viewer, AbilityAimPresentationEventKeys.AreaLine, AreaScopeOffset);
            PublishEnded(actor, viewer, AbilityAimPresentationEventKeys.AreaRectangle, AreaScopeOffset);
            int genericPreviewKeyId = ResolveEventKeyId(AbilityAimPresentationEventKeys.Preview);
            if (_activeSemanticPreviewKeyIdsByActor.Remove(actor.Id, out int activePreviewKeyId) &&
                activePreviewKeyId != genericPreviewKeyId)
            {
                PublishEnded(actor, viewer, activePreviewKeyId, PreviewScopeOffset);
            }

            PublishEnded(actor, viewer, genericPreviewKeyId, PreviewScopeOffset);
            _activeAimSessionKeysByActor.Remove(actor.Id);
            _activeInputSlotsByActor.Remove(actor.Id);
        }

        private void PublishAffectedCollection(
            Entity actor,
            Entity previewTarget,
            in AimImpactDescriptor impact,
            in EffectConfigParams previewParams,
            Vector3 aimWorldCm)
        {
            if (!impact.HasTargetResolver)
            {
                _collections.Replace(
                    actor,
                    EntityCollectionDescriptor.Create(
                        EntityCollectionKeys.AbilityAimAffected,
                        EntityCollectionSourceKind.Explicit,
                        EntityCollectionRoleKind.AimAffected,
                        actor,
                        Entity.Null,
                        "Ability aim affected",
                        "no-target-resolver"),
                    ReadOnlySpan<Entity>.Empty);
                return;
            }

            int dropped = 0;
            int affectedCount;
            TargetQueryDescriptor query = impact.Query;
            switch (query.Kind)
            {
                case TargetResolverKind.BuiltinSpatial:
                    affectedCount = ResolveSpatialAffected(actor, in impact, in previewParams, out dropped);
                    break;
                case TargetResolverKind.GraphProgram:
                    affectedCount = ResolveGraphAffected(actor, previewTarget, in query, in previewParams, aimWorldCm, out dropped);
                    break;
                default:
                    affectedCount = 0;
                    break;
            }
            ReadOnlySpan<Entity> affected = _candidateBuffer.AsSpan(0, affectedCount);

            Span<int> rowRoleIds = _rowRoleIdBuffer.AsSpan(0, affected.Length);
            Span<EntityCollectionRowFlags> rowFlags = _rowFlagBuffer.AsSpan(0, affected.Length);
            for (int i = 0; i < affected.Length; i++)
            {
                rowRoleIds[i] = i == 0 ? PrimaryAimTargetRoleId : 0;
                rowFlags[i] = i == 0 ? EntityCollectionRowFlags.Primary : EntityCollectionRowFlags.None;
            }

            _collections.Replace(
                actor,
                EntityCollectionDescriptor.Create(
                    EntityCollectionKeys.AbilityAimAffected,
                    ResolveCollectionSourceKind(in impact),
                    EntityCollectionRoleKind.AimAffected,
                    actor,
                    affected.Length > 0 ? affected[0] : Entity.Null,
                    "Ability aim affected",
                    dropped > 0 ? $"dropped={dropped}" : "target-query"),
                affected,
                rowRoleIds,
                rowFlags);
        }

        private int ResolveSpatialAffected(
            Entity actor,
            in AimImpactDescriptor impact,
            in EffectConfigParams previewParams,
            out int dropped)
        {
            _fanOutCommands.Clear();
            _budget.NextFrame();
            dropped = 0;
            var ctx = new EffectContext
            {
                RootId = 1,
                Source = actor,
                Target = actor,
                TargetContext = actor
            };
            TargetQueryDescriptor query = impact.Query;
            TargetFilterDescriptor filter = impact.Filter;
            TargetDispatchDescriptor dispatch = impact.Dispatch;
            int candidateCount = TargetResolverFanOutHelper.ResolveTargets(
                _world,
                in ctx,
                in query,
                in previewParams,
                _spatialQueries,
                _candidateBuffer);
            if (candidateCount > 0)
            {
                TargetResolverFanOutHelper.ValidateAndCollect(
                    _world,
                    in ctx,
                    in query,
                    in filter,
                    in dispatch,
                    in previewParams,
                    _candidateBuffer,
                    candidateCount,
                    _budget,
                    _fanOutCommands,
                    ref dropped);
            }

            for (int i = 0; i < _fanOutCommands.Count; i++)
            {
                _candidateBuffer[i] = _fanOutCommands[i].ResolvedEntity;
            }

            return _fanOutCommands.Count;
        }

        private int ResolveGraphAffected(
            Entity actor,
            Entity previewTarget,
            in TargetQueryDescriptor query,
            in EffectConfigParams previewParams,
            Vector3 aimWorldCm,
            out int dropped)
        {
            dropped = 0;
            if (query.GraphProgramId <= 0)
            {
                throw new InvalidOperationException("Ability aim GraphProgram target query requires a positive graph program id.");
            }

            if (_graphPrograms == null || _graphApi == null)
            {
                throw new InvalidOperationException(
                    $"Ability aim GraphProgram target query '{query.GraphProgramId}' requires {nameof(GraphProgramRegistry)} and {nameof(GasGraphRuntimeApi)}.");
            }

            if (!_graphPrograms.TryGetProgram(query.GraphProgramId, out ReadOnlySpan<GraphInstruction> program))
            {
                throw new InvalidOperationException($"Ability aim GraphProgram target query references missing graph id {query.GraphProgramId}.");
            }

            if (program.Length == 0)
            {
                return 0;
            }

            Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = _candidateBuffer;
            entities[0] = actor;
            entities[1] = previewTarget;
            entities[2] = previewTarget;
            var targetList = new GraphTargetList(targets);
            var state = new GraphExecutionState
            {
                World = _world,
                Caster = actor,
                ExplicitTarget = previewTarget,
                TargetContext = previewTarget,
                TargetPos = ToGraphTargetPos(aimWorldCm),
                Api = _graphApi,
                F = floats,
                I = ints,
                B = bools,
                E = entities,
                Targets = targets,
                TargetList = targetList,
            };

            _graphApi.SetConfigContext(in previewParams);
            try
            {
                GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
                return state.TargetList.Count;
            }
            finally
            {
                _graphApi.ClearConfigContext();
            }
        }

        private static EntityCollectionSourceKind ResolveCollectionSourceKind(in AimImpactDescriptor impact)
        {
            return impact.Query.Kind switch
            {
                TargetResolverKind.BuiltinSpatial => EntityCollectionSourceKind.SpatialQuery,
                TargetResolverKind.GraphProgram => EntityCollectionSourceKind.GasGraphResult,
                _ => EntityCollectionSourceKind.Explicit,
            };
        }

        private void PublishAimEvents(
            Entity actor,
            Entity viewer,
            Entity hoveredEntity,
            OrderSelectionType selectionType,
            in AbilityDefinition ability,
            in AimImpactDescriptor impact,
            Vector3 originWorldCm,
            Vector3 aimWorldCm,
            bool valid)
        {
            if (ability.Targeting.CastRangeCm > 0f)
            {
                PublishUpdated(
                    actor,
                    viewer,
                    hoveredEntity,
                    AbilityAimPresentationEventKeys.Range,
                    RangeScopeOffset,
                    originWorldCm,
                    WorldUnits.CmToM(ability.Targeting.CastRangeCm),
                    0f,
                    0f,
                    valid ? 1f : 0f);
            }
            else
            {
                PublishEnded(actor, viewer, AbilityAimPresentationEventKeys.Range, RangeScopeOffset);
            }

            if (impact.Query.Kind == TargetResolverKind.BuiltinSpatial)
            {
                PublishAreaUpdated(actor, viewer, hoveredEntity, selectionType, impact.Query, originWorldCm, aimWorldCm, valid);
            }
            else
            {
                PublishEnded(actor, viewer, AbilityAimPresentationEventKeys.AreaCircle, AreaScopeOffset);
                PublishEnded(actor, viewer, AbilityAimPresentationEventKeys.AreaRing, AreaScopeOffset);
                PublishEnded(actor, viewer, AbilityAimPresentationEventKeys.AreaCone, AreaScopeOffset);
                PublishEnded(actor, viewer, AbilityAimPresentationEventKeys.AreaLine, AreaScopeOffset);
                PublishEnded(actor, viewer, AbilityAimPresentationEventKeys.AreaRectangle, AreaScopeOffset);
            }

            int genericPreviewKeyId = ResolveEventKeyId(AbilityAimPresentationEventKeys.Preview);
            PublishUpdated(
                actor,
                viewer,
                hoveredEntity,
                genericPreviewKeyId,
                PreviewScopeOffset,
                aimWorldCm,
                1f,
                0f,
                0f,
                valid ? 1f : 0f);

            if (!impact.HasSemanticEventKey)
            {
                if (_activeSemanticPreviewKeyIdsByActor.Remove(actor.Id, out int previousSemanticKeyId))
                {
                    PublishEnded(actor, viewer, previousSemanticKeyId, PreviewScopeOffset);
                }

                return;
            }

            int previewKeyId = impact.SemanticEventKeyId;
            if (_activeSemanticPreviewKeyIdsByActor.TryGetValue(actor.Id, out int activePreviewKeyId) &&
                activePreviewKeyId != previewKeyId)
            {
                PublishEnded(actor, viewer, activePreviewKeyId, PreviewScopeOffset);
            }

            _activeSemanticPreviewKeyIdsByActor[actor.Id] = previewKeyId;
            PublishUpdated(
                actor,
                viewer,
                hoveredEntity,
                previewKeyId,
                PreviewScopeOffset,
                aimWorldCm,
                1f,
                0f,
                0f,
                valid ? 1f : 0f);
        }

        private static AimImpactDescriptor ResolveAimImpact(in EffectTemplateData effect)
        {
            int semanticEventKeyId = ResolveSemanticEventKeyId(in effect);
            if (effect.TargetQuery.Kind != TargetResolverKind.None)
            {
                return new AimImpactDescriptor(effect.TargetQuery, effect.TargetFilter, effect.TargetDispatch, semanticEventKeyId);
            }

            if (effect.PresetType == EffectPresetType.LaunchProjectile &&
                effect.Projectile.Range > 0 &&
                effect.Projectile.CollisionHalfWidthCm > 0)
            {
                return new AimImpactDescriptor(
                    new TargetQueryDescriptor
                    {
                        Kind = TargetResolverKind.BuiltinSpatial,
                        Spatial = new BuiltinSpatialDescriptor
                        {
                            Shape = SpatialShape.Line,
                            LengthCm = effect.Projectile.Range,
                            HalfWidthCm = effect.Projectile.CollisionHalfWidthCm,
                        }
                    },
                    new TargetFilterDescriptor
                    {
                        RelationFilter = effect.Projectile.CollisionRelationFilter,
                        ExcludeSource = effect.Projectile.CollisionExcludeSource,
                        MaxTargets = effect.Projectile.MaxHitCount,
                    },
                    new TargetDispatchDescriptor
                    {
                        PayloadEffectTemplateId = effect.Projectile.HitEffectTemplateId > 0
                            ? effect.Projectile.HitEffectTemplateId
                            : effect.Projectile.ImpactEffectTemplateId,
                        ContextMapping = TargetResolverContextMapping.Default,
                    },
                    semanticEventKeyId);
            }

            if (effect.PresetType == EffectPresetType.CreateUnit &&
                (effect.UnitCreation.PlacementRadiusCm > 0 || effect.UnitCreation.OffsetRadius > 0))
            {
                int radiusCm = Math.Max(effect.UnitCreation.PlacementRadiusCm, effect.UnitCreation.OffsetRadius);
                return new AimImpactDescriptor(
                    new TargetQueryDescriptor
                    {
                        Kind = TargetResolverKind.BuiltinSpatial,
                        Spatial = new BuiltinSpatialDescriptor
                        {
                            Shape = SpatialShape.Circle,
                            RadiusCm = radiusCm,
                        }
                    },
                    default,
                    default,
                    semanticEventKeyId);
            }

            return semanticEventKeyId > 0
                ? new AimImpactDescriptor(default, default, default, semanticEventKeyId)
                : default;
        }

        private void WriteAimSessionState(
            Entity actor,
            Entity viewer,
            Entity hovered,
            int abilityId,
            int impactEffectTemplateId,
            int actionKeyId,
            int semanticEventKeyId,
            byte inputSlot,
            byte inputSlotCount,
            Vector3 originWorldCm,
            Vector3 aimWorldCm,
            bool withinCastRange)
        {
            uint revision = 1;
            if (_world.Has<AbilityAimSessionState>(actor))
            {
                revision = _world.Get<AbilityAimSessionState>(actor).Revision + 1;
            }

            var state = new AbilityAimSessionState
            {
                Actor = actor,
                Viewer = viewer,
                HoveredEntity = hovered,
                AbilityId = abilityId,
                ImpactEffectTemplateId = impactEffectTemplateId,
                ActionIdKeyId = actionKeyId,
                SemanticEventKeyId = semanticEventKeyId,
                CurrentInputSlot = inputSlot,
                InputSlotCount = inputSlotCount,
                IsAiming = true,
                IsWithinCastRange = withinCastRange,
                IsValidPlacement = withinCastRange,
                CursorWorldCm = aimWorldCm,
                OriginWorldCm = originWorldCm,
                DirectionDeg = ResolveRotationDeg(originWorldCm, aimWorldCm),
                Revision = revision == 0 ? 1 : revision,
            };

            if (_world.Has<AbilityAimSessionState>(actor))
            {
                _world.Set(actor, state);
            }
            else
            {
                _world.Add(actor, state);
            }
        }

        private void ClearAimSessionState(Entity actor)
        {
            if (_world.IsAlive(actor) && _world.Has<AbilityAimSessionState>(actor))
            {
                _world.Remove<AbilityAimSessionState>(actor);
            }
        }

        private void PublishAimHoverCollection(Entity actor, Entity hovered)
        {
            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.AbilityAimHover,
                EntityCollectionSourceKind.UiHover,
                EntityCollectionRoleKind.AcquisitionPreview,
                actor,
                _world.IsAlive(hovered) ? hovered : Entity.Null,
                "Ability aim hover",
                _world.IsAlive(hovered) ? "aim-hover" : "aim-hover-empty");

            if (_world.IsAlive(hovered))
            {
                Span<Entity> single = stackalloc Entity[1];
                single[0] = hovered;
                Span<int> rowRoleIds = stackalloc int[1];
                rowRoleIds[0] = PrimaryAimTargetRoleId;
                Span<EntityCollectionRowFlags> rowFlags = stackalloc EntityCollectionRowFlags[1];
                rowFlags[0] = EntityCollectionRowFlags.Primary;
                _collections.Replace(actor, descriptor, single, rowRoleIds, rowFlags);
                return;
            }

            _collections.Replace(actor, descriptor, ReadOnlySpan<Entity>.Empty);
        }

        private void PublishLifecycleEvents(
            Entity actor,
            Entity viewer,
            Entity hoveredEntity,
            OrderSelectionType selectionType,
            int abilityId,
            int actionKeyId,
            int previewKeyId,
            in TargetQueryDescriptor query,
            bool hasRange,
            Vector3 originWorldCm,
            Vector3 aimWorldCm,
            byte inputSlot)
        {
            int sessionKey = HashSessionKey(previewKeyId, actionKeyId, abilityId);
            if (!_activeAimSessionKeysByActor.TryGetValue(actor.Id, out int activeSessionKey) ||
                activeSessionKey != sessionKey)
            {
                _activeAimSessionKeysByActor[actor.Id] = sessionKey;
                _activeInputSlotsByActor[actor.Id] = inputSlot;
                PublishLifecycleEventSet(
                    PresentationEventKind.AbilityAimBegun,
                    actor,
                    viewer,
                    hoveredEntity,
                    abilityId,
                    actionKeyId,
                    previewKeyId,
                    selectionType,
                    in query,
                    hasRange,
                    originWorldCm,
                    aimWorldCm,
                    inputSlot,
                    1f);
                return;
            }

            if (_activeInputSlotsByActor.TryGetValue(actor.Id, out byte activeSlot) &&
                activeSlot != inputSlot)
            {
                _activeInputSlotsByActor[actor.Id] = inputSlot;
                PublishLifecycleEventSet(
                    PresentationEventKind.AbilityAimSlotAdvanced,
                    actor,
                    viewer,
                    hoveredEntity,
                    abilityId,
                    actionKeyId,
                    previewKeyId,
                    selectionType,
                    in query,
                    hasRange,
                    originWorldCm,
                    aimWorldCm,
                    inputSlot,
                    1f);
            }
        }

        private void PublishLifecycleEventSet(
            PresentationEventKind kind,
            Entity actor,
            Entity viewer,
            Entity hoveredEntity,
            int abilityId,
            int actionKeyId,
            int previewKeyId,
            OrderSelectionType selectionType,
            in TargetQueryDescriptor query,
            bool hasRange,
            Vector3 originWorldCm,
            Vector3 aimWorldCm,
            byte inputSlot,
            float magnitude)
        {
            if (hasRange)
            {
                PublishLifecycleEvent(
                    kind,
                    actor,
                    viewer,
                    hoveredEntity,
                    ResolveEventKeyId(AbilityAimPresentationEventKeys.Range),
                    RangeScopeOffset,
                    originWorldCm,
                    abilityId,
                    actionKeyId,
                    inputSlot,
                    magnitude);
            }

            string activeAreaEventKey = ResolveActiveAreaEventKey(in query);
            if (!string.IsNullOrEmpty(activeAreaEventKey))
            {
                PublishLifecycleEvent(
                    kind,
                    actor,
                    viewer,
                    hoveredEntity,
                    ResolveEventKeyId(activeAreaEventKey),
                    AreaScopeOffset,
                    ResolveAreaCenter(selectionType, in query, originWorldCm, aimWorldCm),
                    abilityId,
                    actionKeyId,
                    inputSlot,
                    magnitude);
            }

            int genericPreviewKeyId = ResolveEventKeyId(AbilityAimPresentationEventKeys.Preview);
            PublishLifecycleEvent(
                kind,
                actor,
                viewer,
                hoveredEntity,
                genericPreviewKeyId,
                PreviewScopeOffset,
                aimWorldCm,
                abilityId,
                actionKeyId,
                inputSlot,
                magnitude);

            if (previewKeyId > 0 && previewKeyId != genericPreviewKeyId)
            {
                PublishLifecycleEvent(
                    kind,
                    actor,
                    viewer,
                    hoveredEntity,
                    previewKeyId,
                    PreviewScopeOffset,
                    aimWorldCm,
                    abilityId,
                    actionKeyId,
                    inputSlot,
                    magnitude);
            }
        }

        private static int ResolveSemanticEventKeyId(in EffectTemplateData effect)
        {
            return effect.TagId > 0 ? effect.TagId : 0;
        }

        private static string ResolveActiveAreaEventKey(in TargetQueryDescriptor query)
        {
            if (query.Kind != TargetResolverKind.BuiltinSpatial)
            {
                return string.Empty;
            }

            return query.Spatial.Shape switch
            {
                SpatialShape.Circle => AbilityAimPresentationEventKeys.AreaCircle,
                SpatialShape.Ring => AbilityAimPresentationEventKeys.AreaRing,
                SpatialShape.Cone => AbilityAimPresentationEventKeys.AreaCone,
                SpatialShape.Line => AbilityAimPresentationEventKeys.AreaLine,
                SpatialShape.Rectangle => AbilityAimPresentationEventKeys.AreaRectangle,
                _ => string.Empty,
            };
        }

        private static int ResolveEventKeyId(string key)
        {
            int existing = TagRegistry.GetId(key);
            return existing > 0 ? existing : TagRegistry.Register(key);
        }

        private static int ResolveActionKeyId(string actionId)
        {
            return string.IsNullOrWhiteSpace(actionId)
                ? 0
                : TagRegistry.Register($"input.action.{actionId.Trim()}");
        }

        private static Entity ResolveViewer(Entity actor, in AbilityAimInputState input)
        {
            return input.ViewerEntity != Entity.Null ? input.ViewerEntity : actor;
        }

        private Entity ResolveActiveViewer(Entity actor)
        {
            if (_world.IsAlive(actor) && _world.Has<AbilityAimSessionState>(actor))
            {
                Entity viewer = _world.Get<AbilityAimSessionState>(actor).Viewer;
                if (_world.IsAlive(viewer))
                {
                    return viewer;
                }
            }

            return actor;
        }

        private int ResolveAbilityId(Entity actor, InputOrderMapping mapping)
        {
            if (!_world.IsAlive(actor) ||
                !_world.Has<AbilityStateBuffer>(actor) ||
                mapping.ArgsTemplate.I0 is null)
            {
                return 0;
            }

            int slotIndex = mapping.ArgsTemplate.I0.Value;
            ref AbilityStateBuffer abilities = ref _world.Get<AbilityStateBuffer>(actor);
            if ((uint)slotIndex >= (uint)abilities.Count)
            {
                return 0;
            }

            bool hasForm = _world.Has<AbilityFormSlotBuffer>(actor);
            AbilityFormSlotBuffer formSlots = hasForm ? _world.Get<AbilityFormSlotBuffer>(actor) : default;
            bool hasItemGranted = _world.Has<ItemGrantedSlotBuffer>(actor);
            ItemGrantedSlotBuffer itemGranted = hasItemGranted ? _world.Get<ItemGrantedSlotBuffer>(actor) : default;
            bool hasGranted = _world.Has<GrantedSlotBuffer>(actor);
            GrantedSlotBuffer granted = hasGranted ? _world.Get<GrantedSlotBuffer>(actor) : default;
            AbilitySlotState slot = AbilitySlotResolver.Resolve(
                in abilities,
                in formSlots,
                hasForm,
                in itemGranted,
                hasItemGranted,
                in granted,
                hasGranted,
                slotIndex);
            return slot.AbilityId;
        }

        private static byte ResolveInputSlotIndex(AbilityAimInputSlot slot)
        {
            return slot switch
            {
                AbilityAimInputSlot.Target => 0,
                AbilityAimInputSlot.VectorOrigin => 0,
                AbilityAimInputSlot.VectorDirection => 1,
                _ => 0,
            };
        }

        private static byte ResolveInputSlotCount(AbilityAimInputSlot slot)
        {
            return slot is AbilityAimInputSlot.VectorOrigin or AbilityAimInputSlot.VectorDirection ? (byte)2 : (byte)1;
        }

        private static int HashSessionKey(int lifecycleKeyId, int actionKeyId, int abilityId)
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + lifecycleKeyId;
                hash = (hash * 31) + actionKeyId;
                hash = (hash * 31) + abilityId;
                return hash == 0 ? 1 : hash;
            }
        }

        private void PublishAreaUpdated(
            Entity actor,
            Entity viewer,
            Entity hoveredEntity,
            OrderSelectionType selectionType,
            in TargetQueryDescriptor query,
            Vector3 originWorldCm,
            Vector3 aimWorldCm,
            bool valid)
        {
            ref readonly BuiltinSpatialDescriptor spatial = ref query.Spatial;
            Vector3 centerWorldCm = ResolveAreaCenter(selectionType, query, originWorldCm, aimWorldCm);
            string activeKey = spatial.Shape switch
            {
                SpatialShape.Circle => AbilityAimPresentationEventKeys.AreaCircle,
                SpatialShape.Ring => AbilityAimPresentationEventKeys.AreaRing,
                SpatialShape.Cone => AbilityAimPresentationEventKeys.AreaCone,
                SpatialShape.Line => AbilityAimPresentationEventKeys.AreaLine,
                SpatialShape.Rectangle => AbilityAimPresentationEventKeys.AreaRectangle,
                _ => string.Empty,
            };
            PublishAreaEndedExcept(actor, viewer, activeKey);
            switch (spatial.Shape)
            {
                case SpatialShape.Circle:
                    PublishUpdated(
                        actor,
                        viewer,
                        hoveredEntity,
                        AbilityAimPresentationEventKeys.AreaCircle,
                        AreaScopeOffset,
                        centerWorldCm,
                        WorldUnits.CmToM(MathF.Max(0f, spatial.RadiusCm)),
                        0f,
                        0f,
                        valid ? 1f : 0f);
                    break;
                case SpatialShape.Ring:
                    PublishUpdated(
                        actor,
                        viewer,
                        hoveredEntity,
                        AbilityAimPresentationEventKeys.AreaRing,
                        AreaScopeOffset,
                        centerWorldCm,
                        WorldUnits.CmToM(MathF.Max(0f, spatial.RadiusCm)),
                        WorldUnits.CmToM(MathF.Max(0f, spatial.InnerRadiusCm)),
                        0f,
                        valid ? 1f : 0f);
                    break;
                case SpatialShape.Cone:
                    PublishUpdated(
                        actor,
                        viewer,
                        hoveredEntity,
                        AbilityAimPresentationEventKeys.AreaCone,
                        AreaScopeOffset,
                        centerWorldCm,
                        WorldUnits.CmToM(MathF.Max(0f, spatial.RadiusCm)),
                        MathF.Max(0f, spatial.HalfAngleDeg),
                        ResolveRotationDeg(originWorldCm, aimWorldCm),
                        valid ? 1f : 0f);
                    break;
                case SpatialShape.Line:
                    PublishUpdated(
                        actor,
                        viewer,
                        hoveredEntity,
                        AbilityAimPresentationEventKeys.AreaLine,
                        AreaScopeOffset,
                        centerWorldCm,
                        WorldUnits.CmToM(MathF.Max(0f, spatial.LengthCm)),
                        WorldUnits.CmToM(MathF.Max(0f, spatial.HalfWidthCm * 2f)),
                        ResolveRotationDeg(originWorldCm, aimWorldCm),
                        valid ? 1f : 0f);
                    break;
                case SpatialShape.Rectangle:
                    PublishUpdated(
                        actor,
                        viewer,
                        hoveredEntity,
                        AbilityAimPresentationEventKeys.AreaRectangle,
                        AreaScopeOffset,
                        centerWorldCm,
                        WorldUnits.CmToM(MathF.Max(0f, spatial.HalfHeightCm * 2f)),
                        WorldUnits.CmToM(MathF.Max(0f, spatial.HalfWidthCm * 2f)),
                        ResolveRotationDeg(originWorldCm, aimWorldCm),
                        valid ? 1f : 0f);
                    break;
                default:
                    PublishEnded(actor, viewer, AbilityAimPresentationEventKeys.AreaCircle, AreaScopeOffset);
                    PublishEnded(actor, viewer, AbilityAimPresentationEventKeys.AreaRing, AreaScopeOffset);
                    PublishEnded(actor, viewer, AbilityAimPresentationEventKeys.AreaCone, AreaScopeOffset);
                    PublishEnded(actor, viewer, AbilityAimPresentationEventKeys.AreaLine, AreaScopeOffset);
                    PublishEnded(actor, viewer, AbilityAimPresentationEventKeys.AreaRectangle, AreaScopeOffset);
                    break;
            }
        }

        private void PublishAreaEndedExcept(Entity actor, Entity viewer, string activeKey)
        {
            PublishAreaEndedIfNot(actor, viewer, AbilityAimPresentationEventKeys.AreaCircle, activeKey);
            PublishAreaEndedIfNot(actor, viewer, AbilityAimPresentationEventKeys.AreaRing, activeKey);
            PublishAreaEndedIfNot(actor, viewer, AbilityAimPresentationEventKeys.AreaCone, activeKey);
            PublishAreaEndedIfNot(actor, viewer, AbilityAimPresentationEventKeys.AreaLine, activeKey);
            PublishAreaEndedIfNot(actor, viewer, AbilityAimPresentationEventKeys.AreaRectangle, activeKey);
        }

        private void PublishAreaEndedIfNot(Entity actor, Entity viewer, string key, string activeKey)
        {
            if (!string.Equals(key, activeKey, StringComparison.Ordinal))
            {
                PublishEnded(actor, viewer, key, AreaScopeOffset);
            }
        }

        private void PublishUpdated(
            Entity actor,
            Entity viewer,
            Entity target,
            string key,
            int scopeOffset,
            Vector3 worldCm,
            float floatA,
            float floatB,
            float floatC,
            float floatD)
        {
            var evt = new PresentationEvent
            {
                LogicTickStamp = _session?.CurrentTick ?? 0,
                Kind = PresentationEventKind.AbilityAimUpdated,
                KeyId = ResolveEventKeyId(key),
                Source = actor,
                Target = target,
                Viewer = viewer,
                PayloadA = BuildScopeId(actor, scopeOffset),
                PayloadB = scopeOffset,
                Magnitude = floatD,
                FloatA = floatA,
                FloatB = floatB,
                FloatC = floatC,
                FloatD = floatD,
                Position = ToVisualMeters(worldCm),
            };

            if (!_events.TryAdd(in evt))
            {
                throw new InvalidOperationException("PresentationEventStream is full while publishing ability aim update.");
            }
        }

        private void PublishUpdated(
            Entity actor,
            Entity viewer,
            Entity target,
            int keyId,
            int scopeOffset,
            Vector3 worldCm,
            float floatA,
            float floatB,
            float floatC,
            float floatD)
        {
            if (keyId <= 0)
            {
                throw new InvalidOperationException("Ability aim update requires a positive presentation event key id.");
            }

            var evt = new PresentationEvent
            {
                LogicTickStamp = _session?.CurrentTick ?? 0,
                Kind = PresentationEventKind.AbilityAimUpdated,
                KeyId = keyId,
                Source = actor,
                Target = target,
                Viewer = viewer,
                PayloadA = BuildScopeId(actor, scopeOffset),
                PayloadB = scopeOffset,
                Magnitude = floatD,
                FloatA = floatA,
                FloatB = floatB,
                FloatC = floatC,
                FloatD = floatD,
                Position = ToVisualMeters(worldCm),
            };

            if (!_events.TryAdd(in evt))
            {
                throw new InvalidOperationException("PresentationEventStream is full while publishing ability aim update.");
            }
        }

        private void PublishEnded(Entity actor, Entity viewer, string key, int scopeOffset)
        {
            PublishEnded(actor, viewer, ResolveEventKeyId(key), scopeOffset);
        }

        private void PublishEnded(Entity actor, Entity viewer, int keyId, int scopeOffset)
        {
            var evt = new PresentationEvent
            {
                LogicTickStamp = _session?.CurrentTick ?? 0,
                Kind = PresentationEventKind.AbilityAimEnded,
                KeyId = keyId,
                Source = actor,
                Target = actor,
                Viewer = viewer,
                PayloadA = BuildScopeId(actor, scopeOffset),
                PayloadB = scopeOffset,
            };

            if (!_events.TryAdd(in evt))
            {
                throw new InvalidOperationException("PresentationEventStream is full while publishing ability aim end.");
            }
        }

        private void PublishLifecycleEvent(
            PresentationEventKind kind,
            Entity actor,
            Entity viewer,
            Entity target,
            int keyId,
            int scopeOffset,
            Vector3 worldCm,
            int abilityId,
            int actionKeyId,
            byte inputSlot,
            float magnitude)
        {
            var evt = new PresentationEvent
            {
                LogicTickStamp = _session?.CurrentTick ?? 0,
                Kind = kind,
                KeyId = keyId,
                Source = actor,
                Target = target,
                Viewer = viewer,
                PayloadA = BuildScopeId(actor, scopeOffset),
                PayloadB = actionKeyId,
                Magnitude = magnitude,
                FloatA = abilityId,
                FloatB = inputSlot,
                FloatC = scopeOffset,
                FloatD = magnitude,
                Position = ToVisualMeters(worldCm),
            };

            if (!_events.TryAdd(in evt))
            {
                throw new InvalidOperationException($"PresentationEventStream is full while publishing ability aim {kind}.");
            }
        }

        private bool TryResolveAbility(Entity actor, InputOrderMapping mapping, out AbilityDefinition definition)
        {
            definition = default;
            if (!_world.IsAlive(actor) ||
                !_world.Has<AbilityStateBuffer>(actor) ||
                mapping.ArgsTemplate.I0 is null)
            {
                return false;
            }

            int slotIndex = mapping.ArgsTemplate.I0.Value;
            ref AbilityStateBuffer abilities = ref _world.Get<AbilityStateBuffer>(actor);
            if ((uint)slotIndex >= (uint)abilities.Count)
            {
                return false;
            }

            bool hasForm = _world.Has<AbilityFormSlotBuffer>(actor);
            AbilityFormSlotBuffer formSlots = hasForm ? _world.Get<AbilityFormSlotBuffer>(actor) : default;
            bool hasItemGranted = _world.Has<ItemGrantedSlotBuffer>(actor);
            ItemGrantedSlotBuffer itemGranted = hasItemGranted ? _world.Get<ItemGrantedSlotBuffer>(actor) : default;
            bool hasGranted = _world.Has<GrantedSlotBuffer>(actor);
            GrantedSlotBuffer granted = hasGranted ? _world.Get<GrantedSlotBuffer>(actor) : default;
            AbilitySlotState slot = AbilitySlotResolver.Resolve(
                in abilities,
                in formSlots,
                hasForm,
                in itemGranted,
                hasItemGranted,
                in granted,
                hasGranted,
                slotIndex);
            return slot.AbilityId > 0 && _abilities.TryGet(slot.AbilityId, out definition);
        }

        private bool TryResolveActorWorld(Entity actor, out Vector3 worldCm)
        {
            worldCm = default;
            if (!_world.IsAlive(actor))
            {
                return false;
            }

            if (_world.Has<WorldPositionCm>(actor))
            {
                WorldCmInt2 cm = _world.Get<WorldPositionCm>(actor).ToWorldCmInt2();
                worldCm = new Vector3(cm.X, 0f, cm.Y);
                return true;
            }

            if (_world.Has<VisualTransform>(actor))
            {
                Vector3 visual = _world.Get<VisualTransform>(actor).Position;
                worldCm = new Vector3(WorldUnits.MToCm(visual.X), 0f, WorldUnits.MToCm(visual.Z));
                return true;
            }

            return false;
        }

        private static EffectConfigParams BuildPreviewParams(Vector3 originWorldCm, Vector3 targetWorldCm)
        {
            EffectConfigParams result = default;
            result.TryAddFloat(EffectParamKeys.TargetOriginX, originWorldCm.X);
            result.TryAddFloat(EffectParamKeys.TargetOriginY, originWorldCm.Z);
            result.TryAddFloat(EffectParamKeys.TargetPosX, targetWorldCm.X);
            result.TryAddFloat(EffectParamKeys.TargetPosY, targetWorldCm.Z);
            return result;
        }

        private static Vector3 ResolveAimWorldCm(Vector3 actorWorldCm, in AbilityAimInputState input)
        {
            if (input.HoveredEntity != Entity.Null && input.HasCursorWorldCm)
            {
                return input.CursorWorldCm;
            }

            return input.HasCursorWorldCm ? input.CursorWorldCm : actorWorldCm;
        }

        private static Vector3 ResolveOriginWorldCm(Vector3 actorWorldCm, in AbilityAimInputState input)
        {
            return input.HasOriginWorldCm ? input.OriginWorldCm : actorWorldCm;
        }

        private static bool ClampTargetToRange(Vector3 originWorldCm, ref Vector3 targetWorldCm, float rangeCm)
        {
            if (rangeCm <= 0f)
            {
                return true;
            }

            float dx = targetWorldCm.X - originWorldCm.X;
            float dz = targetWorldCm.Z - originWorldCm.Z;
            float distance = MathF.Sqrt((dx * dx) + (dz * dz));
            if (distance <= rangeCm + 0.01f || distance <= 0.001f)
            {
                return true;
            }

            float scale = rangeCm / distance;
            targetWorldCm = new Vector3(originWorldCm.X + (dx * scale), targetWorldCm.Y, originWorldCm.Z + (dz * scale));
            return false;
        }

        private static Vector3 ResolveAreaCenter(OrderSelectionType selectionType, in TargetQueryDescriptor query, Vector3 originWorldCm, Vector3 aimWorldCm)
        {
            if (query.Kind == TargetResolverKind.BuiltinSpatial &&
                (query.Spatial.Shape == SpatialShape.Cone ||
                 query.Spatial.Shape == SpatialShape.Line ||
                 query.Spatial.Shape == SpatialShape.Rectangle))
            {
                return originWorldCm;
            }

            return selectionType == OrderSelectionType.None ? originWorldCm : aimWorldCm;
        }

        private static float ResolveRotationDeg(Vector3 fromWorldCm, Vector3 toWorldCm)
        {
            float dx = toWorldCm.X - fromWorldCm.X;
            float dz = toWorldCm.Z - fromWorldCm.Z;
            if ((dx * dx) + (dz * dz) <= 0.001f)
            {
                return 0f;
            }

            return WorldPlane2D.FacingDegreesPositiveFromDirection((int)MathF.Round(dx), (int)MathF.Round(dz));
        }

        private static Vector3 ToVisualMeters(Vector3 worldCm)
        {
            return new Vector3(WorldUnits.CmToM(worldCm.X), OverlayY, WorldUnits.CmToM(worldCm.Z));
        }

        private static IntVector2 ToGraphTargetPos(Vector3 worldCm)
        {
            return new IntVector2(
                (int)MathF.Round(worldCm.X, MidpointRounding.AwayFromZero),
                (int)MathF.Round(worldCm.Z, MidpointRounding.AwayFromZero));
        }

        private static int BuildScopeId(Entity owner, int offset)
        {
            unchecked
            {
                int scope = (owner.Id * 100000) + offset;
                return scope <= 0 ? offset : scope;
            }
        }
    }
}
