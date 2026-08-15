using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.EntityQueries;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Input.Orders;
using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Registry;
using Ludots.Core.Spatial;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class AbilityAimPresentationRuntimeTests
    {
        private const int AreaScopeOffset = 44000;
        private const int PreviewScopeOffset = 44002;

        private World _world = null!;
        private AbilityDefinitionRegistry _abilities = null!;
        private EffectTemplateRegistry _effects = null!;
        private EntityCollectionStore _collections = null!;
        private RecordingSpatialQueryService _spatialQueries = null!;
        private PresentationEventStream _events = null!;
        private AbilityAimPresentationRuntime _runtime = null!;

        [SetUp]
        public void SetUp()
        {
            TagRegistry.Clear();
            ConfigKeyRegistry.Clear();
            EffectParamKeys.Initialize();
            _world = World.Create();
            _abilities = new AbilityDefinitionRegistry();
            _effects = new EffectTemplateRegistry();
            _collections = new EntityCollectionStore(new StringIntRegistry());
            _spatialQueries = new RecordingSpatialQueryService();
            _events = new PresentationEventStream(512);
            _runtime = new AbilityAimPresentationRuntime(
                _world,
                _abilities,
                _effects,
                _collections,
                _spatialQueries,
                _events);
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
        }

        [Test]
        public void UpdateAiming_CircleImpact_PublishesAffectedCollectionAndAimEvents()
        {
            int effectId = RegisterEffect(2001, "Effect.Test.Circle", SpatialShape.Circle, radiusCm: 120, innerRadiusCm: 0);
            RegisterAbility(1001, castRangeCm: 500f, effectId);
            Entity actor = CreateActor(1001);
            Entity viewer = _world.Create();
            Entity target = _world.Create(WorldPositionCm.FromCm(300, 400));
            _spatialQueries.NextResults.Add(target);

            _runtime.UpdateAiming(
                actor,
                CreateMapping(OrderTargetType.Position),
                CreateInput(new Vector3(300f, 0f, 400f), hoveredEntity: target, viewerEntity: viewer));

            Assert.That(_spatialQueries.LastRadiusCenter, Is.EqualTo(new WorldCmInt2(300, 400)));
            Assert.That(_spatialQueries.LastRadiusCm, Is.EqualTo(120));
            Assert.That(_world.Has<AbilityAimSessionState>(actor), Is.True);
            AbilityAimSessionState session = _world.Get<AbilityAimSessionState>(actor);
            Assert.That(session.Actor, Is.EqualTo(actor));
            Assert.That(session.Viewer, Is.EqualTo(viewer));
            Assert.That(session.HoveredEntity, Is.EqualTo(target));
            Assert.That(session.AbilityId, Is.EqualTo(1001));
            Assert.That(session.ImpactEffectTemplateId, Is.EqualTo(effectId));
            Assert.That(session.CurrentInputSlot, Is.EqualTo(0));
            Assert.That(session.InputSlotCount, Is.EqualTo(1));
            Assert.That(session.IsAiming, Is.True);
            Assert.That(session.IsWithinCastRange, Is.True);
            Assert.That(session.CursorWorldCm, Is.EqualTo(new Vector3(300f, 0f, 400f)));
            Assert.That(session.OriginWorldCm, Is.EqualTo(Vector3.Zero));
            Assert.That(_collections.TryGetView(actor, EntityCollectionKeys.AbilityAimHover, out EntityCollectionView hoverView), Is.True);
            Assert.That(hoverView.Role, Is.EqualTo(EntityCollectionRoleKind.AcquisitionPreview));
            Assert.That(hoverView.SourceKind, Is.EqualTo(EntityCollectionSourceKind.UiHover));
            Assert.That(hoverView.PrimaryEntity, Is.EqualTo(target));
            Assert.That(_collections.TryGetView(actor, EntityCollectionKeys.AbilityAimAffected, out EntityCollectionView view), Is.True);
            Assert.That(view.Role, Is.EqualTo(EntityCollectionRoleKind.AimAffected));
            Assert.That(view.SourceKind, Is.EqualTo(EntityCollectionSourceKind.SpatialQuery));
            Assert.That(view.PrimaryEntity, Is.EqualTo(target));
            Span<Entity> affected = stackalloc Entity[4];
            Assert.That(_collections.CopyEntities(actor, EntityCollectionKeys.AbilityAimAffected, affected), Is.EqualTo(1));
            Assert.That(affected[0], Is.EqualTo(target));
            Assert.That(
                _collections.TryGet(actor, EntityCollectionKeys.AbilityAimAffected, out EntityCollectionHandle handle),
                Is.True);
            Assert.That(
                _collections.TryGetRowAt(
                    handle,
                    0,
                    out Entity rowEntity,
                    out int ordinal,
                    out int roleId,
                    out EntityCollectionRowFlags flags),
                Is.True);
            Assert.That(rowEntity, Is.EqualTo(target));
            Assert.That(ordinal, Is.EqualTo(0));
            Assert.That(roleId, Is.EqualTo(1));
            Assert.That(flags, Is.EqualTo(EntityCollectionRowFlags.Primary));

            PresentationEvent range = SingleEvent(PresentationEventKind.AbilityAimUpdated, AbilityAimPresentationEventKeys.Range);
            Assert.That(range.Source, Is.EqualTo(actor));
            Assert.That(range.Position.X, Is.EqualTo(0f).Within(0.001f));
            Assert.That(range.Position.Z, Is.EqualTo(0f).Within(0.001f));
            Assert.That(range.FloatA, Is.EqualTo(5f).Within(0.001f));
            Assert.That(range.FloatD, Is.EqualTo(1f));

            PresentationEvent area = SingleEvent(PresentationEventKind.AbilityAimUpdated, AbilityAimPresentationEventKeys.AreaCircle);
            Assert.That(area.Position.X, Is.EqualTo(3f).Within(0.001f));
            Assert.That(area.Position.Z, Is.EqualTo(4f).Within(0.001f));
            Assert.That(area.FloatA, Is.EqualTo(1.2f).Within(0.001f));

            PresentationEvent preview = SingleEvent(PresentationEventKind.AbilityAimUpdated, AbilityAimPresentationEventKeys.Preview);
            Assert.That(preview.Position.X, Is.EqualTo(3f).Within(0.001f));
            Assert.That(preview.Position.Z, Is.EqualTo(4f).Within(0.001f));

            PresentationEvent areaBegun = SingleEvent(PresentationEventKind.AbilityAimBegun, AbilityAimPresentationEventKeys.AreaCircle);
            Assert.That(areaBegun.Source, Is.EqualTo(actor));
            Assert.That(areaBegun.Target, Is.EqualTo(target));
            Assert.That(areaBegun.FloatA, Is.EqualTo(1001f));
            Assert.That(areaBegun.FloatB, Is.EqualTo(0f));
            Assert.That(areaBegun.PayloadB, Is.EqualTo(TagRegistry.GetId("input.action.Skill")));
            PresentationEvent semanticBegun = SingleEvent(PresentationEventKind.AbilityAimBegun, "Effect.Test.Circle");
            Assert.That(semanticBegun.PayloadA, Is.EqualTo(PreviewScope(actor)));
        }

        [Test]
        public void UpdateAiming_RingImpact_UsesImpactEffectInnerRadius()
        {
            int effectId = RegisterEffect(2002, "Effect.Test.Ring", SpatialShape.Ring, radiusCm: 250, innerRadiusCm: 120);
            RegisterAbility(1002, castRangeCm: 650f, effectId);
            Entity actor = CreateActor(1002);

            _runtime.UpdateAiming(
                actor,
                CreateMapping(OrderTargetType.Position),
                CreateInput(new Vector3(100f, 0f, 200f)));

            PresentationEvent area = SingleEvent(PresentationEventKind.AbilityAimUpdated, AbilityAimPresentationEventKeys.AreaRing);
            Assert.That(_spatialQueries.LastRadiusCenter, Is.EqualTo(new WorldCmInt2(100, 200)));
            Assert.That(area.FloatA, Is.EqualTo(2.5f).Within(0.001f));
            Assert.That(area.FloatB, Is.EqualTo(1.2f).Within(0.001f));
        }

        [Test]
        public void UpdateAiming_LineImpact_ClampsInvalidAimAndPublishesRotation()
        {
            int effectId = RegisterEffect(
                2003,
                "Effect.Test.Line",
                SpatialShape.Line,
                radiusCm: 0,
                innerRadiusCm: 0,
                lengthCm: 600,
                halfWidthCm: 40);
            RegisterAbility(1003, castRangeCm: 400f, effectId);
            Entity actor = CreateActor(1003);

            _runtime.UpdateAiming(
                actor,
                CreateMapping(OrderTargetType.Direction),
                CreateInput(new Vector3(800f, 0f, 0f), AbilityAimInputSlot.VectorDirection));

            PresentationEvent area = SingleEvent(PresentationEventKind.AbilityAimUpdated, AbilityAimPresentationEventKeys.AreaLine);
            PresentationEvent preview = SingleEvent(PresentationEventKind.AbilityAimUpdated, AbilityAimPresentationEventKeys.Preview);
            Assert.That(_spatialQueries.LastLineOrigin, Is.EqualTo(new WorldCmInt2(0, 0)));
            Assert.That(_spatialQueries.LastLineLengthCm, Is.EqualTo(600));
            Assert.That(area.Position.X, Is.EqualTo(0f).Within(0.001f));
            Assert.That(area.FloatA, Is.EqualTo(6f).Within(0.001f));
            Assert.That(area.FloatB, Is.EqualTo(0.8f).Within(0.001f));
            Assert.That(area.FloatC, Is.EqualTo(0f).Within(0.001f));
            Assert.That(preview.Position.X, Is.EqualTo(4f).Within(0.001f));
            Assert.That(preview.FloatD, Is.EqualTo(0f));
        }

        [Test]
        public void UpdateAiming_VectorSlotAdvance_PublishesSlotAdvancedAndUpdatesSession()
        {
            int effectId = RegisterEffect(
                2010,
                "Effect.Test.VectorAdvance",
                SpatialShape.Line,
                radiusCm: 0,
                innerRadiusCm: 0,
                lengthCm: 600,
                halfWidthCm: 40);
            RegisterAbility(1010, castRangeCm: 1000f, effectId);
            Entity actor = CreateActor(1010);

            _runtime.UpdateAiming(
                actor,
                CreateMapping(OrderTargetType.Direction),
                new AbilityAimInputState(
                    slot: AbilityAimInputSlot.VectorOrigin,
                    hasCursorWorldCm: true,
                    cursorWorldCm: new Vector3(100f, 0f, 100f),
                    hasOriginWorldCm: false,
                    originWorldCm: default,
                    hoveredEntity: Entity.Null));
            _runtime.UpdateAiming(
                actor,
                CreateMapping(OrderTargetType.Direction),
                new AbilityAimInputState(
                    slot: AbilityAimInputSlot.VectorDirection,
                    hasCursorWorldCm: true,
                    cursorWorldCm: new Vector3(400f, 0f, 100f),
                    hasOriginWorldCm: true,
                    originWorldCm: new Vector3(100f, 0f, 100f),
                    hoveredEntity: Entity.Null));

            AbilityAimSessionState session = _world.Get<AbilityAimSessionState>(actor);
            Assert.That(session.CurrentInputSlot, Is.EqualTo(1));
            Assert.That(session.InputSlotCount, Is.EqualTo(2));
            Assert.That(session.OriginWorldCm, Is.EqualTo(new Vector3(100f, 0f, 100f)));
            Assert.That(session.CursorWorldCm, Is.EqualTo(new Vector3(400f, 0f, 100f)));
            Assert.That(session.DirectionDeg, Is.EqualTo(0f).Within(0.001f));
            Assert.That(
                _events.GetSpan().ToArray().Count(evt => evt.Kind == PresentationEventKind.AbilityAimBegun),
                Is.EqualTo(4));
            PresentationEvent advanced = SingleEvent(PresentationEventKind.AbilityAimSlotAdvanced, AbilityAimPresentationEventKeys.AreaLine);
            Assert.That(advanced.FloatA, Is.EqualTo(1010f));
            Assert.That(advanced.FloatB, Is.EqualTo(1f));
        }

        [Test]
        public void UpdateAiming_CreateUnitWithEffectTag_PublishesSemanticPreviewEvent()
        {
            int effectTagId = TagRegistry.Register("Effect.Rts.Sc2.Warp");
            int effectId = 2004;
            _effects.Register(effectId, new EffectTemplateData
            {
                TagId = effectTagId,
                PresetType = EffectPresetType.CreateUnit,
                UnitCreation = new UnitCreationDescriptor
                {
                    Count = 1,
                    OffsetRadius = 260,
                    PlacementRadiusCm = 260,
                }
            });
            RegisterAbility(1004, castRangeCm: 2600f, effectId);
            Entity actor = CreateActor(1004);

            _runtime.UpdateAiming(
                actor,
                CreateMapping(OrderTargetType.Position),
                CreateInput(new Vector3(500f, 0f, 0f)));

            PresentationEvent genericPreview = SingleEvent(PresentationEventKind.AbilityAimUpdated, AbilityAimPresentationEventKeys.Preview);
            PresentationEvent semanticPreview = SingleEvent(PresentationEventKind.AbilityAimUpdated, "Effect.Rts.Sc2.Warp");
            PresentationEvent area = SingleEvent(PresentationEventKind.AbilityAimUpdated, AbilityAimPresentationEventKeys.AreaCircle);
            Assert.That(semanticPreview.KeyId, Is.EqualTo(effectTagId));
            Assert.That(semanticPreview.PayloadA, Is.EqualTo(genericPreview.PayloadA));
            Assert.That(area.FloatA, Is.EqualTo(2.6f).Within(0.001f));
        }

        [Test]
        public void UpdateAiming_TagOnlyImpact_UsesExplicitEmptyAffectedCollection()
        {
            int effectTagId = TagRegistry.Register("Effect.Test.RelationOnlyPreview");
            const int effectId = 2006;
            _effects.Register(effectId, new EffectTemplateData
            {
                TagId = effectTagId,
                PresetType = EffectPresetType.Buff,
            });
            RegisterAbility(1006, castRangeCm: 700f, effectId);
            Entity actor = CreateActor(1006);

            _runtime.UpdateAiming(
                actor,
                CreateMapping(OrderTargetType.Position),
                CreateInput(new Vector3(300f, 0f, 0f)));

            Assert.That(_collections.TryGetView(actor, EntityCollectionKeys.AbilityAimAffected, out var view), Is.True);
            Assert.That(view.SourceKind, Is.EqualTo(EntityCollectionSourceKind.Explicit));
            Assert.That(view.Count, Is.EqualTo(0));
            Assert.That(view.PrimaryEntity, Is.EqualTo(Entity.Null));
            PresentationEvent semanticPreview = SingleEvent(PresentationEventKind.AbilityAimUpdated, "Effect.Test.RelationOnlyPreview");
            Assert.That(semanticPreview.KeyId, Is.EqualTo(effectTagId));
        }

        [Test]
        public void UpdateAiming_GraphProgramImpact_WritesGasGraphAffectedCollection()
        {
            const int graphProgramId = 3001;
            const int effectId = 2007;
            var graphSetup = CreateRelationshipGraphRuntime();
            var graphPrograms = new GraphProgramRegistry();
            graphPrograms.Register(
                graphProgramId,
                new[]
                {
                    new GraphInstruction
                    {
                        Op = (ushort)GraphNodeOp.RelationshipQueryOutgoing,
                        A = 0,
                        Dst = (byte)graphSetup.AssistTypeId,
                    },
                    new GraphInstruction
                    {
                        Op = (ushort)GraphNodeOp.RelationshipSortByMetric,
                        A = 0,
                        Dst = (byte)graphSetup.AssistTypeId,
                        Imm = graphSetup.PriorityMetricId,
                        Flags = 1,
                    },
                    new GraphInstruction
                    {
                        Op = (ushort)GraphNodeOp.HaltReturnInt,
                    },
                }, GraphKind.Query);
            _runtime = new AbilityAimPresentationRuntime(
                _world,
                _abilities,
                _effects,
                _collections,
                _spatialQueries,
                _events,
                session: null,
                graphPrograms,
                graphSetup.Api);
            _effects.Register(effectId, new EffectTemplateData
            {
                TagId = TagRegistry.Register("Effect.Test.GraphPreview"),
                TargetQuery = new TargetQueryDescriptor
                {
                    Kind = TargetResolverKind.GraphProgram,
                    GraphProgramId = graphProgramId,
                }
            });
            RegisterAbility(1007, castRangeCm: 900f, effectId);
            Entity actor = CreateActor(1007);
            Entity lowPriority = _world.Create(WorldPositionCm.FromCm(100, 0));
            Entity highPriority = _world.Create(WorldPositionCm.FromCm(200, 0));
            graphSetup.Relationships.SetMetric(actor, lowPriority, graphSetup.AssistTypeId, graphSetup.PriorityMetricId, 10);
            graphSetup.Relationships.SetMetric(actor, highPriority, graphSetup.AssistTypeId, graphSetup.PriorityMetricId, 90);

            _runtime.UpdateAiming(
                actor,
                CreateMapping(OrderTargetType.Entity),
                CreateInput(new Vector3(250f, 0f, 0f)));

            Assert.That(_collections.TryGetView(actor, EntityCollectionKeys.AbilityAimAffected, out EntityCollectionView view), Is.True);
            Assert.That(view.Role, Is.EqualTo(EntityCollectionRoleKind.AimAffected));
            Assert.That(view.SourceKind, Is.EqualTo(EntityCollectionSourceKind.GasGraphResult));
            Assert.That(view.Count, Is.EqualTo(2));
            Assert.That(view.PrimaryEntity, Is.EqualTo(highPriority));
            Span<Entity> affected = stackalloc Entity[4];
            Assert.That(_collections.CopyEntities(actor, EntityCollectionKeys.AbilityAimAffected, affected), Is.EqualTo(2));
            Assert.That(affected[0], Is.EqualTo(highPriority));
            Assert.That(affected[1], Is.EqualTo(lowPriority));
            Assert.That(_collections.TryGet(actor, EntityCollectionKeys.AbilityAimAffected, out EntityCollectionHandle handle), Is.True);
            Assert.That(
                _collections.TryGetRowAt(
                    handle,
                    0,
                    out Entity rowEntity,
                    out int ordinal,
                    out int roleId,
                    out EntityCollectionRowFlags flags),
                Is.True);
            Assert.That(rowEntity, Is.EqualTo(highPriority));
            Assert.That(ordinal, Is.EqualTo(0));
            Assert.That(roleId, Is.EqualTo(1));
            Assert.That(flags, Is.EqualTo(EntityCollectionRowFlags.Primary));
            Assert.That(_events.GetSpan().ToArray().Any(evt => evt.Kind == PresentationEventKind.AbilityAimUpdated), Is.True);
        }

        [Test]
        public void PerformerRules_ConsumeAimEvents_AndReuseScopedPerformer()
        {
            int effectId = RegisterEffect(
                2005,
                "Effect.Test.RuleLine",
                SpatialShape.Line,
                radiusCm: 0,
                innerRadiusCm: 0,
                lengthCm: 600,
                halfWidthCm: 40);
            RegisterAbility(1005, castRangeCm: 400f, effectId);
            Entity actor = CreateActor(1005);
            var fixture = new PerformerFixture(_world, _events, _collections.KeyRegistry);

            _runtime.UpdateAiming(
                actor,
                CreateMapping(OrderTargetType.Direction),
                CreateInput(new Vector3(800f, 0f, 0f), AbilityAimInputSlot.VectorDirection));
            fixture.Tick();

            Entity area = fixture.FindPerformer("test.aim.area.line");
            Entity preview = fixture.FindPerformer("test.aim.preview");
            Assert.That(_world.Get<PerformerWorldPosition>(area).Value.X, Is.EqualTo(0f).Within(0.001f));
            Assert.That(fixture.Runtime.ResolveFloat(area, WellKnownPerformerParamKeys.OverlayLength, -1f), Is.EqualTo(6f).Within(0.001f));
            Assert.That(fixture.Runtime.ResolveFloat(area, WellKnownPerformerParamKeys.OverlayWidth, -1f), Is.EqualTo(0.8f).Within(0.001f));
            Assert.That(_world.Get<PerformerWorldPosition>(preview).Value.X, Is.EqualTo(4f).Within(0.001f));
            Assert.That(fixture.Runtime.ResolveFloat(preview, WellKnownPerformerParamKeys.MarkerScale, -1f), Is.EqualTo(1f).Within(0.001f));

            _runtime.UpdateAiming(
                actor,
                CreateMapping(OrderTargetType.Direction),
                CreateInput(new Vector3(300f, 0f, 0f), AbilityAimInputSlot.VectorDirection));
            fixture.Tick();

            Assert.That(fixture.Runtime.ActiveCount, Is.EqualTo(2));
            Assert.That(fixture.FindPerformer("test.aim.area.line"), Is.EqualTo(area));
            Assert.That(fixture.FindPerformer("test.aim.preview"), Is.EqualTo(preview));
            Assert.That(_world.Get<PerformerWorldPosition>(preview).Value.X, Is.EqualTo(3f).Within(0.001f));

            _runtime.Clear(actor);
            fixture.Tick();

            Assert.That(_collections.TryGet(actor, EntityCollectionKeys.AbilityAimAffected, out _), Is.False);
            Assert.That(_collections.TryGet(actor, EntityCollectionKeys.AbilityAimHover, out _), Is.False);
            Assert.That(_world.Has<AbilityAimSessionState>(actor), Is.False);
            Assert.That(fixture.Runtime.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void EntityCollectionDiff_PublishesRowLifecycleEvents()
        {
            Entity actor = _world.Create();
            Entity first = _world.Create(WorldPositionCm.FromCm(100, 200));
            Entity second = _world.Create(WorldPositionCm.FromCm(300, 400));
            var collectionEvents = new EntityCollectionPresentationEventSystem(_world, _collections, _events);

            Span<Entity> rows = stackalloc Entity[] { first, second };
            Span<int> roleIds = stackalloc int[] { 1, 2 };
            Span<EntityCollectionRowFlags> flags = stackalloc EntityCollectionRowFlags[]
            {
                EntityCollectionRowFlags.Primary,
                EntityCollectionRowFlags.None,
            };
            _collections.Replace(
                actor,
                EntityCollectionDescriptor.Create(
                    EntityCollectionKeys.AbilityAimAffected,
                    EntityCollectionSourceKind.SpatialQuery,
                    EntityCollectionRoleKind.AimAffected,
                    actor,
                    first),
                rows,
                roleIds,
                flags);

            collectionEvents.Update(0.016f);

            PresentationEvent[] added = _events.GetSpan()
                .ToArray()
                .Where(evt => evt.Kind == PresentationEventKind.EntityCollectionMemberAdded)
                .ToArray();
            Assert.That(added.Length, Is.EqualTo(2));
            Assert.That(added[0].KeyId, Is.EqualTo(_collections.KeyRegistry.GetId(EntityCollectionKeys.AbilityAimAffected)));
            Assert.That(added[0].Source, Is.EqualTo(first));
            Assert.That(added[0].Target, Is.EqualTo(actor));
            Assert.That(added[0].Viewer, Is.EqualTo(actor));
            Assert.That(added[0].PayloadB, Is.EqualTo(1));
            Assert.That(added[0].Magnitude, Is.EqualTo((float)EntityCollectionRowFlags.Primary));
            Assert.That(added[0].FloatA, Is.EqualTo(0f));
            Assert.That(added[1].Source, Is.EqualTo(second));
            Assert.That(added[1].PayloadB, Is.EqualTo(2));

            _events.Clear();
            _collections.Replace(
                actor,
                EntityCollectionDescriptor.Create(
                    EntityCollectionKeys.AbilityAimAffected,
                    EntityCollectionSourceKind.SpatialQuery,
                    EntityCollectionRoleKind.AimAffected,
                    actor,
                    second),
                rows.Slice(1, 1),
                roleIds.Slice(1, 1),
                flags.Slice(1, 1));

            collectionEvents.Update(0.016f);

            PresentationEvent removed = SingleEvent(PresentationEventKind.EntityCollectionMemberRemoved, EntityCollectionKeys.AbilityAimAffected);
            Assert.That(removed.Source, Is.EqualTo(first));
            Assert.That(removed.Target, Is.EqualTo(actor));
            Assert.That(removed.PayloadA, Is.GreaterThan(0));
        }

        [Test]
        public void PerformerRules_ConsumeCollectionRows_AsEntityAttachedHighlights()
        {
            Entity actor = _world.Create();
            Entity first = _world.Create(WorldPositionCm.FromCm(100, 200), new VisualTransform { Position = new Vector3(1f, 0f, 2f) });
            Entity second = _world.Create(WorldPositionCm.FromCm(300, 400), new VisualTransform { Position = new Vector3(3f, 0f, 4f) });
            var collectionEvents = new EntityCollectionPresentationEventSystem(_world, _collections, _events);
            var fixture = new PerformerFixture(_world, _events, _collections.KeyRegistry);

            Span<Entity> rows = stackalloc Entity[] { first, second };
            Span<int> roleIds = stackalloc int[] { 1, 0 };
            Span<EntityCollectionRowFlags> flags = stackalloc EntityCollectionRowFlags[]
            {
                EntityCollectionRowFlags.Primary,
                EntityCollectionRowFlags.None,
            };
            _collections.Replace(
                actor,
                EntityCollectionDescriptor.Create(
                    EntityCollectionKeys.AbilityAimAffected,
                    EntityCollectionSourceKind.SpatialQuery,
                    EntityCollectionRoleKind.AimAffected,
                    actor,
                    first),
                rows,
                roleIds,
                flags);

            collectionEvents.Update(0.016f);
            PresentationEvent[] initialAdded = _events.GetSpan()
                .ToArray()
                .Where(evt => evt.Kind == PresentationEventKind.EntityCollectionMemberAdded)
                .ToArray();
            Assert.That(initialAdded.Length, Is.EqualTo(2));
            int firstScope = initialAdded.Single(evt => evt.Source == first).PayloadA;
            int secondScope = initialAdded.Single(evt => evt.Source == second).PayloadA;
            Assert.That(firstScope, Is.Not.EqualTo(secondScope));
            fixture.Tick();

            Entity firstHighlight = fixture.FindPerformer("test.collection.highlight", first);
            Entity secondHighlight = fixture.FindPerformer("test.collection.highlight", second);
            Assert.That(_world.Get<PerformerState>(firstHighlight).OwnerEntity, Is.EqualTo(first));
            Assert.That(_world.Get<PerformerState>(secondHighlight).OwnerEntity, Is.EqualTo(second));
            Assert.That(
                fixture.Runtime.ResolveFloat(firstHighlight, WellKnownPerformerParamKeys.MarkerScale, -1f),
                Is.EqualTo(1f));

            _collections.Replace(
                actor,
                EntityCollectionDescriptor.Create(
                    EntityCollectionKeys.AbilityAimAffected,
                    EntityCollectionSourceKind.SpatialQuery,
                    EntityCollectionRoleKind.AimAffected,
                    actor,
                    second),
                rows.Slice(1, 1),
                roleIds.Slice(1, 1),
                flags.Slice(1, 1));

            collectionEvents.Update(0.016f);
            PresentationEvent[] removed = _events.GetSpan()
                .ToArray()
                .Where(evt => evt.Kind == PresentationEventKind.EntityCollectionMemberRemoved)
                .ToArray();
            Assert.That(removed.Length, Is.EqualTo(1));
            Assert.That(removed[0].Source, Is.EqualTo(first));
            Assert.That(removed[0].PayloadA, Is.EqualTo(firstScope));
            fixture.Tick();

            Assert.That(_world.IsAlive(firstHighlight), Is.False);
            Assert.That(_world.IsAlive(secondHighlight), Is.True);
            Assert.That(fixture.Runtime.ActiveCount, Is.EqualTo(1));
        }

        [Test]
        public void EntityCollectionDiff_UsesAimSessionViewer_ForCollectionRowEvents()
        {
            Entity actor = _world.Create();
            Entity viewer = _world.Create(new Ludots.Core.Gameplay.Components.Team { Id = 1 });
            Entity target = _world.Create(
                WorldPositionCm.FromCm(100, 200),
                new Ludots.Core.Gameplay.Components.Team { Id = 2 });
            _world.Add(actor, new AbilityAimSessionState { Actor = actor, Viewer = viewer, IsAiming = true });
            var collectionEvents = new EntityCollectionPresentationEventSystem(_world, _collections, _events);

            Span<Entity> rows = stackalloc Entity[] { target };
            _collections.Replace(
                actor,
                EntityCollectionDescriptor.Create(
                    EntityCollectionKeys.AbilityAimAffected,
                    EntityCollectionSourceKind.SpatialQuery,
                    EntityCollectionRoleKind.AimAffected,
                    actor,
                    target),
                rows);

            collectionEvents.Update(0.016f);

            PresentationEvent added = SingleEvent(PresentationEventKind.EntityCollectionMemberAdded, EntityCollectionKeys.AbilityAimAffected);
            Assert.That(added.Source, Is.EqualTo(target));
            Assert.That(added.Target, Is.EqualTo(actor));
            Assert.That(added.Viewer, Is.EqualTo(viewer));
        }

        [Test]
        public void PerformerRules_PreserveViewerContext_AndResolveRelationshipColor()
        {
            TeamRelationshipSnapshot relationships = TeamManager.CaptureSnapshot();
            try
            {
                TeamManager.Clear();
                TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);
                TeamManager.SetRelationshipSymmetric(3, 2, TeamRelationship.Friendly);

                Entity actor = _world.Create();
                Entity hostileViewer = _world.Create(new Ludots.Core.Gameplay.Components.Team { Id = 1 });
                Entity friendlyViewer = _world.Create(new Ludots.Core.Gameplay.Components.Team { Id = 3 });
                Entity target = _world.Create(
                    WorldPositionCm.FromCm(100, 200),
                    new VisualTransform { Position = new Vector3(1f, 0f, 2f) },
                    new Ludots.Core.Gameplay.Components.Team { Id = 2 });
                var collectionEvents = new EntityCollectionPresentationEventSystem(_world, _collections, _events);
                var fixture = new PerformerFixture(_world, _events, _collections.KeyRegistry);

                _world.Add(actor, new AbilityAimSessionState { Actor = actor, Viewer = hostileViewer, IsAiming = true });
                ReplaceSingleAffected(actor, target);
                collectionEvents.Update(0.016f);
                fixture.Tick();

                Entity hostileHighlight = fixture.FindPerformer("test.collection.highlight", target);
                Assert.That(_world.Has<PerformerRelationContext>(hostileHighlight), Is.True);
                Assert.That(_world.Get<PerformerRelationContext>(hostileHighlight).Viewer, Is.EqualTo(hostileViewer));
                Assert.That(_world.Get<PerformerRelationContext>(hostileHighlight).Target, Is.EqualTo(actor));
                Assert.That(
                    fixture.Runtime.ResolveVector(hostileHighlight, WellKnownPerformerParamKeys.MarkerColorR, Vector4.Zero),
                    Is.EqualTo(TeamColorResolver.Team2Color));

                _events.Clear();
                _world.Set(actor, new AbilityAimSessionState { Actor = actor, Viewer = friendlyViewer, IsAiming = true });
                _collections.Remove(actor, EntityCollectionKeys.AbilityAimAffected);
                collectionEvents.Update(0.016f);
                fixture.Tick();

                ReplaceSingleAffected(actor, target);
                collectionEvents.Update(0.016f);
                fixture.Tick();

                Entity friendlyHighlight = fixture.FindPerformer("test.collection.highlight", target);
                Assert.That(_world.Get<PerformerRelationContext>(friendlyHighlight).Viewer, Is.EqualTo(friendlyViewer));
                Assert.That(
                    fixture.Runtime.ResolveVector(friendlyHighlight, WellKnownPerformerParamKeys.MarkerColorR, Vector4.Zero),
                    Is.EqualTo(TeamColorResolver.Team1Color));
            }
            finally
            {
                TeamManager.RestoreSnapshot(relationships);
            }
        }

        private void ReplaceSingleAffected(Entity actor, Entity target)
        {
            Span<Entity> rows = stackalloc Entity[] { target };
            Span<int> roleIds = stackalloc int[] { 1 };
            Span<EntityCollectionRowFlags> flags = stackalloc EntityCollectionRowFlags[] { EntityCollectionRowFlags.Primary };
            _collections.Replace(
                actor,
                EntityCollectionDescriptor.Create(
                    EntityCollectionKeys.AbilityAimAffected,
                    EntityCollectionSourceKind.SpatialQuery,
                    EntityCollectionRoleKind.AimAffected,
                    actor,
                    target),
                rows,
                roleIds,
                flags);
        }

        private Entity CreateActor(int abilityId)
        {
            var abilities = new AbilityStateBuffer();
            abilities.AddAbility(abilityId);
            return _world.Create(
                WorldPositionCm.FromCm(0, 0),
                new FacingDirection { AngleRad = 0f },
                abilities);
        }

        private static InputOrderMapping CreateMapping(OrderTargetType TargetType)
        {
            return new InputOrderMapping
            {
                ActionId = "Skill",
                TargetType = TargetType,
                ArgsTemplate = new OrderArgsTemplate { I0 = 0 }
            };
        }

        private static AbilityAimInputState CreateInput(
            Vector3 cursorWorldCm,
            AbilityAimInputSlot slot = AbilityAimInputSlot.Target,
            Entity hoveredEntity = default,
            Entity viewerEntity = default)
        {
            return new AbilityAimInputState(
                slot,
                hasCursorWorldCm: true,
                cursorWorldCm,
                hasOriginWorldCm: false,
                originWorldCm: default,
                hoveredEntity,
                viewerEntity);
        }

        private void RegisterAbility(int abilityId, float castRangeCm, int impactEffectTemplateId)
        {
            _abilities.Register(abilityId, new AbilityDefinition
            {
                HasTargeting = true,
                Targeting = new AbilityTargetingConfig
                {
                    CastRangeCm = castRangeCm,
                    ImpactEffectTemplateId = impactEffectTemplateId,
                }
            });
        }

        private int RegisterEffect(
            int effectId,
            string effectTag,
            SpatialShape shape,
            int radiusCm,
            int innerRadiusCm,
            int lengthCm = 0,
            int halfWidthCm = 0)
        {
            _effects.Register(effectId, new EffectTemplateData
            {
                TagId = TagRegistry.Register(effectTag),
                TargetQuery = new TargetQueryDescriptor
                {
                    Kind = TargetResolverKind.BuiltinSpatial,
                    Spatial = new BuiltinSpatialDescriptor
                    {
                        Shape = shape,
                        RadiusCm = radiusCm,
                        InnerRadiusCm = innerRadiusCm,
                        LengthCm = lengthCm,
                        HalfWidthCm = halfWidthCm,
                    }
                },
                TargetFilter = new TargetFilterDescriptor
                {
                    RelationFilter = RelationshipFilter.All,
                },
                TargetDispatch = new TargetDispatchDescriptor
                {
                    ContextMapping = TargetResolverContextMapping.Default,
                }
            });
            return effectId;
        }

        private RelationshipGraphRuntimeSetup CreateRelationshipGraphRuntime()
        {
            var typeRegistry = new RelationshipTypeRegistry();
            var metricRegistry = new RelationshipMetricRegistry();
            var flagRegistry = new RelationshipFlagRegistry();
            var reasonRegistry = new RelationshipReasonRegistry();
            var bandRegistry = new RelationshipBandRegistry();
            var changeBuffer = new RelationshipChangeBuffer();
            var relationships = new RelationshipRuntime(
                _world,
                typeRegistry,
                metricRegistry,
                flagRegistry,
                bandRegistry,
                changeBuffer,
                new RelationshipReverseIndex(_world));
            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry(), new GasBudget());
            var entityQueries = new EntitySetQueryRuntime(_world, tagOps, relationships);
            int assistTypeId = typeRegistry.Register("Assist");
            int priorityMetricId = metricRegistry.Register("Priority", 0, 100, 0);
            var api = new GasGraphRuntimeApi(
                _world,
                tagOps: tagOps,
                relationshipRuntime: relationships,
                typeRegistry: typeRegistry,
                metricRegistry: metricRegistry,
                flagRegistry: flagRegistry,
                reasonRegistry: reasonRegistry,
                entityQueries: entityQueries);

            return new RelationshipGraphRuntimeSetup(api, relationships, assistTypeId, priorityMetricId);
        }

        private sealed record RelationshipGraphRuntimeSetup(
            GasGraphRuntimeApi Api,
            RelationshipRuntime Relationships,
            int AssistTypeId,
            int PriorityMetricId);

        private PresentationEvent SingleEvent(PresentationEventKind kind, string key)
        {
            int keyId = kind is PresentationEventKind.EntityCollectionMemberAdded or PresentationEventKind.EntityCollectionMemberRemoved
                ? _collections.KeyRegistry.GetId(key)
                : TagRegistry.GetId(key);
            Assert.That(keyId, Is.GreaterThan(0), $"Event key '{key}' should be registered for {kind}.");
            var events = _events.GetSpan().ToArray()
                .Where(evt => evt.Kind == kind && evt.KeyId == keyId)
                .ToArray();
            Assert.That(events.Length, Is.EqualTo(1), $"Expected one {kind}:{key} event.");
            return events[0];
        }

        private static int PreviewScope(Entity actor) => (actor.Id * 100000) + PreviewScopeOffset;

        private sealed class PerformerFixture
        {
            private readonly World _world;
            public readonly PerformerDefinitionRegistry Definitions;
            public readonly PerformerEntityRuntime Runtime;
            private readonly PresentationEventStream _events;
            private readonly PerformerCommandBuffer _commands;
            private readonly StringIntRegistry _entityCollectionKeyRegistry;
            private readonly PerformerRuleSystem _rules;
            private readonly PerformerRuntimeSystem _runtimeSystem;
            private readonly PerformerBehaviorSystem _behaviorSystem;

            public PerformerFixture(
                World world,
                PresentationEventStream events,
                StringIntRegistry entityCollectionKeyRegistry)
            {
                _world = world;
                Definitions = new PerformerDefinitionRegistry();
                Runtime = new PerformerEntityRuntime(world);
                _events = events ?? throw new ArgumentNullException(nameof(events));
                _entityCollectionKeyRegistry = entityCollectionKeyRegistry ?? throw new ArgumentNullException(nameof(entityCollectionKeyRegistry));
                _commands = new PerformerCommandBuffer(256);
                RegisterDefinitions();
                var graphApi = new GasGraphRuntimeApi(world, spatialQueries: null, coords: null, eventBus: null);
                _rules = new PerformerRuleSystem(
                    world,
                    _events,
                    _commands,
                    Definitions,
                    Runtime,
                    new GraphProgramRegistry(),
                    graphApi,
                    new Dictionary<string, object>());
                _runtimeSystem = new PerformerRuntimeSystem(
                    world,
                    _commands,
                    _events,
                    new TransientMarkerBuffer(),
                    new PresentationRequestBuffer(),
                    Runtime,
                    new PresentationStableIdAllocator(),
                    Definitions);
                _behaviorSystem = new PerformerBehaviorSystem(
                    world,
                    Runtime,
                    Definitions,
                    _events,
                    new PresentationOwnerChangeBuffer(64),
                    new SoundRequestBuffer());
            }

            public void Tick()
            {
                _rules.Update(0.016f);
                _runtimeSystem.Update(0.016f);
                _behaviorSystem.Update(0.016f);
                _events.Clear();
            }

            public Entity FindPerformer(string performerId)
            {
                return FindPerformer(performerId, owner: null);
            }

            public Entity FindPerformer(string performerId, Entity owner)
            {
                return FindPerformer(performerId, (Entity?)owner);
            }

            private Entity FindPerformer(string performerId, Entity? owner)
            {
                int defId = Definitions.GetId(performerId);
                Entity found = Entity.Null;
                var query = new QueryDescription().WithAll<PerformerState>();
                _world.Query(in query, (Entity entity, ref PerformerState state) =>
                {
                    if (state.DefId == defId && (owner == null || state.OwnerEntity == owner.Value))
                    {
                        found = entity;
                    }
                });

                Assert.That(found, Is.Not.EqualTo(Entity.Null), $"Performer '{performerId}' was not created.");
                return found;
            }

            private void RegisterDefinitions()
            {
                int areaId = Definitions.GetOrRegisterId("test.aim.area.line");
                int previewId = Definitions.GetOrRegisterId("test.aim.preview");
                int highlightId = Definitions.GetOrRegisterId("test.collection.highlight");
                int affectedCollectionKeyId = _entityCollectionKeyRegistry.Register(EntityCollectionKeys.AbilityAimAffected);
                Definitions.Register("test.aim.area.line", new PerformerDefinition
                {
                    DefaultLifetime = -1f,
                    ParamDefaults = new[]
                    {
                        new ParamDefault { ParamKey = WellKnownPerformerParamKeys.OverlayLength, Lane = ParamLane.Float, FloatValue = 1f },
                        new ParamDefault { ParamKey = WellKnownPerformerParamKeys.OverlayWidth, Lane = ParamLane.Float, FloatValue = 1f },
                    },
                    Behaviors = SingleGroundOverlayBehavior(),
                });
                Definitions.Register("test.aim.preview", new PerformerDefinition
                {
                    DefaultLifetime = -1f,
                    ParamDefaults = new[]
                    {
                        new ParamDefault { ParamKey = WellKnownPerformerParamKeys.MarkerScale, Lane = ParamLane.Float, FloatValue = 1f },
                    },
                    Behaviors = SingleMeshBehavior(),
                });
                Definitions.Register("test.collection.highlight", new PerformerDefinition
                {
                    DefaultLifetime = -1f,
                    ParamDefaults = new[]
                    {
                        new ParamDefault { ParamKey = WellKnownPerformerParamKeys.MarkerScale, Lane = ParamLane.Float, FloatValue = 1f },
                    },
                    Bindings = new[]
                    {
                        new PerformerParamBinding
                        {
                            ParamKey = WellKnownPerformerParamKeys.MarkerColorR,
                            Value = ValueRef.FromEntityColorVector(),
                        },
                    },
                    Behaviors = SingleMeshBehavior(),
                });
                Definitions.Register("test.aim.rules", new PerformerDefinition
                {
                    Rules = new[]
                    {
                        CreateRule(AbilityAimPresentationEventKeys.AreaLine, areaId),
                        FloatParamRule(AbilityAimPresentationEventKeys.AreaLine, areaId, WellKnownPerformerParamKeys.OverlayLength, PerformerCommandValueSource.EventFloatA),
                        FloatParamRule(AbilityAimPresentationEventKeys.AreaLine, areaId, WellKnownPerformerParamKeys.OverlayWidth, PerformerCommandValueSource.EventFloatB),
                        EndRule(AbilityAimPresentationEventKeys.AreaLine, areaId),
                        CreateRule(AbilityAimPresentationEventKeys.Preview, previewId),
                        FloatParamRule(AbilityAimPresentationEventKeys.Preview, previewId, WellKnownPerformerParamKeys.MarkerScale, PerformerCommandValueSource.EventFloatA),
                        EndRule(AbilityAimPresentationEventKeys.Preview, previewId),
                        CollectionCreateRule(affectedCollectionKeyId, highlightId),
                        CollectionEndRule(affectedCollectionKeyId, highlightId),
                    }
                });
            }

            private static PerformerRule CreateRule(string key, int definitionId)
            {
                return new PerformerRule
                {
                    Event = new EventFilter { Kind = PresentationEventKind.AbilityAimBegun, KeyId = TagRegistry.Register(key) },
                    Command = new PerformerCommand
                    {
                        CommandKind = PerformerCommandKind.CreatePerformer,
                        PerformerDefinitionId = definitionId,
                        ScopeSource = PerformerCommandScopeSource.EventPayloadA,
                        UseEventPosition = true,
                    }
                };
            }

            private static PerformerRule FloatParamRule(string key, int definitionId, int paramKey, PerformerCommandValueSource source)
            {
                return new PerformerRule
                {
                    Event = new EventFilter { Kind = PresentationEventKind.AbilityAimUpdated, KeyId = TagRegistry.Register(key) },
                    Command = new PerformerCommand
                    {
                        CommandKind = PerformerCommandKind.SetParam,
                        PerformerDefinitionId = definitionId,
                        ScopeSource = PerformerCommandScopeSource.EventPayloadA,
                        UseEventPosition = true,
                        ParamKey = paramKey,
                        ParamLane = ParamLane.Float,
                        ValueSource = source,
                    }
                };
            }

            private static PerformerRule EndRule(string key, int definitionId)
            {
                return new PerformerRule
                {
                    Event = new EventFilter { Kind = PresentationEventKind.AbilityAimEnded, KeyId = TagRegistry.Register(key) },
                    Command = new PerformerCommand
                    {
                        CommandKind = PerformerCommandKind.DestroyScopedPerformer,
                        PerformerDefinitionId = definitionId,
                        ScopeSource = PerformerCommandScopeSource.EventPayloadA,
                        UseEventPosition = true,
                    }
                };
            }

            private static PerformerRule CollectionCreateRule(int keyId, int definitionId)
            {
                return new PerformerRule
                {
                    Event = new EventFilter { Kind = PresentationEventKind.EntityCollectionMemberAdded, KeyId = keyId },
                    Command = new PerformerCommand
                    {
                        CommandKind = PerformerCommandKind.CreatePerformer,
                        PerformerDefinitionId = definitionId,
                        ScopeSource = PerformerCommandScopeSource.EventPayloadA,
                        OwnerSource = PerformerCommandEntitySource.EventSource,
                        UseEventPosition = false,
                    }
                };
            }

            private static PerformerRule CollectionEndRule(int keyId, int definitionId)
            {
                return new PerformerRule
                {
                    Event = new EventFilter { Kind = PresentationEventKind.EntityCollectionMemberRemoved, KeyId = keyId },
                    Command = new PerformerCommand
                    {
                        CommandKind = PerformerCommandKind.DestroyScopedPerformer,
                        PerformerDefinitionId = definitionId,
                        ScopeSource = PerformerCommandScopeSource.EventPayloadA,
                        OwnerSource = PerformerCommandEntitySource.EventSource,
                        UseEventPosition = false,
                    }
                };
            }

            private static BehaviorSlot[] SingleGroundOverlayBehavior()
            {
                return new[]
                {
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.GroundOverlay,
                            AssetId = (int)GroundOverlayShape.Line,
                            RenderPath = VisualRenderPath.None,
                            Mobility = VisualMobility.Movable,
                            LocalScale = Vector3.One,
                        }
                    }
                };
            }

            private static BehaviorSlot[] SingleMeshBehavior()
            {
                return new[]
                {
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.Mesh,
                            AssetId = 1,
                            RenderPath = VisualRenderPath.StaticMesh,
                            Mobility = VisualMobility.Movable,
                            LocalScale = Vector3.One,
                        }
                    }
                };
            }
        }

        private sealed class RecordingSpatialQueryService : ISpatialQueryService
        {
            public readonly List<Entity> NextResults = new();
            public WorldCmInt2 LastRadiusCenter { get; private set; }
            public int LastRadiusCm { get; private set; }
            public WorldCmInt2 LastLineOrigin { get; private set; }
            public int LastLineLengthCm { get; private set; }

            public SpatialQueryResult QueryAabb(in WorldAabbCm bounds, Span<Entity> buffer)
            {
                return WriteResults(buffer);
            }

            public SpatialQueryResult QueryRadius(WorldCmInt2 center, int radiusCm, Span<Entity> buffer)
            {
                LastRadiusCenter = center;
                LastRadiusCm = radiusCm;
                return WriteResults(buffer);
            }

            public SpatialQueryResult QueryCone(WorldCmInt2 origin, int directionDeg, int halfAngleDeg, int rangeCm, Span<Entity> buffer)
            {
                return WriteResults(buffer);
            }

            public SpatialQueryResult QueryRectangle(WorldCmInt2 center, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer)
            {
                return WriteResults(buffer);
            }

            public SpatialQueryResult QueryLine(WorldCmInt2 origin, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer)
            {
                LastLineOrigin = origin;
                LastLineLengthCm = lengthCm;
                return WriteResults(buffer);
            }

            public SpatialQueryResult QueryHexRange(HexCoordinates center, int hexRadius, Span<Entity> buffer)
            {
                return WriteResults(buffer);
            }

            public SpatialQueryResult QueryHexRing(HexCoordinates center, int hexRadius, Span<Entity> buffer)
            {
                return WriteResults(buffer);
            }

            private SpatialQueryResult WriteResults(Span<Entity> buffer)
            {
                int count = Math.Min(buffer.Length, NextResults.Count);
                for (int i = 0; i < count; i++)
                {
                    buffer[i] = NextResults[i];
                }

                int dropped = Math.Max(0, NextResults.Count - buffer.Length);
                NextResults.Clear();
                return new SpatialQueryResult(count, dropped);
            }
        }
    }
}
