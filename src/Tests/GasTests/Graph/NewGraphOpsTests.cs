using System;
using Arch.Core;
using Ludots.Core.EntityQueries;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Unit tests for new Graph operations added during the architecture overhaul:
    ///   - LoadContextSource / LoadContextTarget / LoadContextTargetContext
    ///   - ApplyEffectDynamic / FanOutApplyEffectDynamic
    /// </summary>
    [TestFixture]
    public class NewGraphOpsTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            EffectParamKeys.Initialize();
        }

        // ════════════════════════════════════════════════════════════════════
        //  LoadContextSource / LoadContextTarget / LoadContextTargetContext
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void GraphOps_LoadContextSource_LoadsFromExecutionState()
        {
            using var world = World.Create();
            var caster = world.Create();
            var target = world.Create();
            var api = new GasGraphRuntimeApi(world, null, null, null);

            // LoadContextSource loads the caster entity into an entity register
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.LoadContextSource, Dst = 2 },
            };

            var result = ExecuteAndGetEntity(world, api, caster, target, program, entityReg: 2);
            That(result, Is.EqualTo(caster));
        }

        [Test]
        public void GraphOps_LoadContextTarget_LoadsFromExecutionState()
        {
            using var world = World.Create();
            var caster = world.Create();
            var target = world.Create();
            var api = new GasGraphRuntimeApi(world, null, null, null);

            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.LoadContextTarget, Dst = 3 },
            };

            var result = ExecuteAndGetEntity(world, api, caster, target, program, entityReg: 3);
            That(result, Is.EqualTo(target));
        }

        [Test]
        public void GraphOps_LoadContextTargetContext_LoadsFromExecutionState()
        {
            using var world = World.Create();
            var caster = world.Create();
            var target = world.Create();
            var targetCtx = world.Create();
            var api = new GasGraphRuntimeApi(world, null, null, null);

            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.LoadContextTargetContext, Dst = 4 },
            };

            var result = ExecuteAndGetEntityWithTargetContext(
                world, api, caster, target, targetCtx, program, entityReg: 4);
            That(result, Is.EqualTo(targetCtx));
        }

        [Test]
        public void GraphCompiler_LoadContextTarget_ThenRemoveEffectTemplate_Compiles()
        {
            var cfg = new GraphConfig
            {
                Id = "Test.RemoveEffectTemplate",
                Kind = "Effect",
                Entry = "target",
                Nodes =
                {
                    new GraphNodeConfig { Id = "target", Op = "LoadContextTarget", Next = "remove" },
                    new GraphNodeConfig { Id = "remove", Op = "RemoveEffectTemplate", EffectTemplate = "Effect.Test.Mark", Inputs = { "target" } },
                }
            };

            var (pkg, diags) = GraphCompiler.Compile(cfg);

            That(diags, Is.Empty);
            That(pkg.HasValue, Is.True);
            That((GraphNodeOp)pkg!.Value.Program[0].Op, Is.EqualTo(GraphNodeOp.LoadContextTarget));
            That((GraphNodeOp)pkg.Value.Program[1].Op, Is.EqualTo(GraphNodeOp.RemoveEffectTemplate));
        }

        // ════════════════════════════════════════════════════════════════════
        //  ApplyEffectDynamic
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void GraphOps_ApplyEffectDynamic_PublishesEffectRequest()
        {
            using var world = World.Create();
            var requests = new EffectRequestQueue();
            var api = new GasGraphRuntimeApi(world, null, null, null, effectRequests: requests);

            var caster = world.Create();
            var target = world.Create();

            // I[0] = templateId, E[0] = caster, E[1] = target
            // ApplyEffectDynamic: source=Caster(implicit), target=E[A], templateId=I[B]
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.LoadCaster, Dst = 0 },
                new() { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 1 },
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 42 },
                new() { Op = (ushort)GraphNodeOp.ApplyEffectDynamic, A = 1, B = 0 },
            };

            ExecuteProgram(world, api, caster, target, program);

            That(requests.Count, Is.EqualTo(1));
            var req = requests[0];
            That(req.TemplateId, Is.EqualTo(42));
            That(req.Source, Is.EqualTo(caster));
            That(req.Target, Is.EqualTo(target));
        }

        // ════════════════════════════════════════════════════════════════════
        //  FanOutApplyEffectDynamic
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void GraphOps_FanOutApplyEffectDynamic_PublishesForAllTargets()
        {
            using var world = World.Create();
            var requests = new EffectRequestQueue();
            var api = new GasGraphRuntimeApi(world, null, null, null, effectRequests: requests);

            var caster = world.Create();
            var t1 = world.Create();
            var t2 = world.Create();
            var t3 = world.Create();

            // Build target list with 3 targets, then FanOut with templateId from I[B]
            // FanOutApplyEffectDynamic: source=E[A], templateId=I[B], targets=TargetList
            var f = new float[GraphVmLimits.MaxFloatRegisters];
            var iArr = new int[GraphVmLimits.MaxIntRegisters];
            var b = new byte[GraphVmLimits.MaxBoolRegisters];
            var e = new Entity[GraphVmLimits.MaxEntityRegisters];
            var targets = new Entity[GraphVmLimits.MaxTargets];

            e[0] = caster;
            e[1] = t1; // not used directly
            targets[0] = t1;
            targets[1] = t2;
            targets[2] = t3;
            iArr[0] = 99; // templateId

            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                ExplicitTarget = t1,
                TargetPosCm = default,
                Api = api,
                F = f,
                I = iArr,
                B = b,
                E = e,
                Targets = targets,
                TargetList = new GraphTargetList(targets) { Count = 3 },
            };

            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.FanOutApplyEffectDynamic, A = 0, B = 0 },
            };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);

            That(requests.Count, Is.EqualTo(3));
            That(requests[0].TemplateId, Is.EqualTo(99));
            That(requests[0].Source, Is.EqualTo(caster));
            That(requests[0].Target, Is.EqualTo(t1));
            That(requests[1].Target, Is.EqualTo(t2));
            That(requests[2].Target, Is.EqualTo(t3));
        }

        // ════════════════════════════════════════════════════════════════════
        //  Combined: LoadConfig → ApplyEffectDynamic
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void GraphOps_LoadConfigEffectId_ThenDynamicApply_WorksEndToEnd()
        {
            using var world = World.Create();
            var requests = new EffectRequestQueue();
            var api = new GasGraphRuntimeApi(world, null, null, null, effectRequests: requests);

            var caster = world.Create();
            var target = world.Create();

            // Set up config context with a payload effect ID
            var config = new EffectConfigParams();
            int payloadKey = EffectParamKeys.PayloadEffectId;
            config.TryAddEffectTemplateId(payloadKey, 777);
            api.SetConfigContext(in config);

            // Graph: load payload effect ID from config, then apply it dynamically
            // ApplyEffectDynamic: source=Caster(implicit), target=E[A], templateId=I[B]
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.LoadCaster, Dst = 0 },
                new() { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 1 },
                new() { Op = (ushort)GraphNodeOp.LoadConfigEffectId, Dst = 0, Imm = payloadKey },
                new() { Op = (ushort)GraphNodeOp.ApplyEffectDynamic, A = 1, B = 0 },
            };

            ExecuteProgram(world, api, caster, target, program);
            api.ClearConfigContext();

            That(requests.Count, Is.EqualTo(1));
            That(requests[0].TemplateId, Is.EqualTo(777));
        }

        [Test]
        public void GraphOps_RemoveEffectTemplate_MarksMatchingActiveEffectForCancellation()
        {
            using var world = World.Create();
            var api = new GasGraphRuntimeApi(world, null, null, null);

            var target = world.Create(new ActiveEffectContainer());
            var effect = world.Create(
                new GameplayEffect { LifetimeKind = EffectLifetimeKind.After, ClockId = GasClockId.FixedFrame, AggregatesModifiers = true },
                new EffectTemplateRef { TemplateId = 91 });

            ref var container = ref world.Get<ActiveEffectContainer>(target);
            That(container.Add(effect), Is.True);

            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.LoadContextTarget, Dst = 2 },
                new() { Op = (ushort)GraphNodeOp.RemoveEffectTemplate, A = 2, Imm = 91 },
            };

            ExecuteProgram(world, api, caster: Entity.Null, target, program);

            That(world.Get<GameplayEffect>(effect).CancelRequested, Is.True);
            That(world.Has<AttributeAggregateDirty>(target), Is.True);
        }

        // ════════════════════════════════════════════════════════════════════
        //  Helpers
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void GraphOps_RelationshipCrudAndTypedReadWrite_Work()
        {
            using var world = World.Create();
            RelationshipApiSetup relationshipSetup = CreateRelationshipApi(world);
            var source = world.Create();
            var target = world.Create();

            var state = CreateState(world, relationshipSetup.Api, source, target);
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 42 },
                new() { Op = (ushort)GraphNodeOp.ConstBool, Dst = 0, Imm = 1 },
                new() { Op = (ushort)GraphNodeOp.RelationshipEnsureLink, A = 0, B = 1, Dst = (byte)relationshipSetup.SocialBondTypeId },
                new() { Op = (ushort)GraphNodeOp.RelationshipSetMetric, A = 0, B = 1, C = 0, Imm = relationshipSetup.LoyaltyMetricId, Dst = byte.MaxValue, Flags = (byte)relationshipSetup.SocialBondTypeId },
                new() { Op = (ushort)GraphNodeOp.RelationshipSetFlag, A = 0, B = 1, C = 0, Imm = relationshipSetup.TrustedFlagId, Dst = byte.MaxValue, Flags = (byte)relationshipSetup.SocialBondTypeId },
                new() { Op = (ushort)GraphNodeOp.RelationshipGetMetric, A = 0, B = 1, Dst = 1, Imm = relationshipSetup.LoyaltyMetricId, Flags = (byte)relationshipSetup.SocialBondTypeId },
                new() { Op = (ushort)GraphNodeOp.RelationshipHasFlag, A = 0, B = 1, Dst = 1, Imm = relationshipSetup.TrustedFlagId, Flags = (byte)relationshipSetup.SocialBondTypeId },
            };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);

            That(relationshipSetup.Runtime.GetMetric(source, target, relationshipSetup.SocialBondTypeId, relationshipSetup.LoyaltyMetricId), Is.EqualTo(42));
            That(relationshipSetup.Runtime.HasFlag(source, target, relationshipSetup.SocialBondTypeId, relationshipSetup.TrustedFlagId), Is.True);
            That(state.I[1], Is.EqualTo(42));
            That(state.B[1], Is.EqualTo(1));
        }

        [Test]
        public void GraphOps_RelationshipQueryFilterSortAndAggregate_Work()
        {
            using var world = World.Create();
            RelationshipApiSetup relationshipSetup = CreateRelationshipApi(world);
            var source = world.Create();
            var low = world.Create();
            var high = world.Create();
            var mid = world.Create();

            relationshipSetup.Runtime.SetMetric(source, low, relationshipSetup.SocialBondTypeId, relationshipSetup.LoyaltyMetricId, 20, reasonId: 0);
            relationshipSetup.Runtime.SetMetric(source, high, relationshipSetup.SocialBondTypeId, relationshipSetup.LoyaltyMetricId, 70, reasonId: 0);
            relationshipSetup.Runtime.SetMetric(source, mid, relationshipSetup.SocialBondTypeId, relationshipSetup.LoyaltyMetricId, 45, reasonId: 0);
            relationshipSetup.Runtime.SetFlag(source, low, relationshipSetup.SocialBondTypeId, relationshipSetup.TrustedFlagId, true);
            relationshipSetup.Runtime.SetFlag(source, high, relationshipSetup.SocialBondTypeId, relationshipSetup.TrustedFlagId, true);

            var state = CreateState(world, relationshipSetup.Api, source, low);
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 10f },
                new() { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = 80f },
                new() { Op = (ushort)GraphNodeOp.RelationshipQueryOutgoing, A = 0, Dst = (byte)relationshipSetup.SocialBondTypeId },
                new() { Op = (ushort)GraphNodeOp.RelationshipFilterFlag, A = 0, Dst = (byte)relationshipSetup.SocialBondTypeId, Imm = relationshipSetup.TrustedFlagId, Flags = 1 },
                new() { Op = (ushort)GraphNodeOp.RelationshipFilterMetricRange, A = 0, B = 0, C = 1, Dst = (byte)relationshipSetup.SocialBondTypeId, Imm = relationshipSetup.LoyaltyMetricId },
                new() { Op = (ushort)GraphNodeOp.RelationshipSortByMetric, A = 0, Dst = (byte)relationshipSetup.SocialBondTypeId, Imm = relationshipSetup.LoyaltyMetricId, Flags = 1 },
                new() { Op = (ushort)GraphNodeOp.RelationshipAggSumMetric, A = 0, Dst = 2, Imm = relationshipSetup.LoyaltyMetricId, Flags = (byte)relationshipSetup.SocialBondTypeId },
                new() { Op = (ushort)GraphNodeOp.RelationshipAggMaxMetric, A = 0, Dst = 3, Imm = relationshipSetup.LoyaltyMetricId, Flags = (byte)relationshipSetup.SocialBondTypeId },
                new() { Op = (ushort)GraphNodeOp.RelationshipAggAverageMetric, A = 0, Dst = 4, Imm = relationshipSetup.LoyaltyMetricId, Flags = (byte)relationshipSetup.SocialBondTypeId },
            };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);

            That(state.TargetList.Count, Is.EqualTo(2));
            That(state.TargetList.Span[0], Is.EqualTo(high));
            That(state.TargetList.Span[1], Is.EqualTo(low));
            That(state.I[2], Is.EqualTo(90));
            That(state.I[3], Is.EqualTo(70));
            That(state.I[4], Is.EqualTo(45));
        }

        [Test]
        public void GraphOps_RelationshipQueryCanFanOutDynamicPayloadEffects()
        {
            using var world = World.Create();
            var requests = new EffectRequestQueue();
            RelationshipApiSetup relationshipSetup = CreateRelationshipApi(world, requests);
            var source = world.Create();
            var allyA = world.Create();
            var allyB = world.Create();

            relationshipSetup.Runtime.SetMetric(source, allyA, relationshipSetup.SocialBondTypeId, relationshipSetup.SupportMetricId, 55, reasonId: 0);
            relationshipSetup.Runtime.SetMetric(source, allyB, relationshipSetup.SocialBondTypeId, relationshipSetup.SupportMetricId, 80, reasonId: 0);

            var state = CreateState(world, relationshipSetup.Api, source, allyA);
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.RelationshipQueryOutgoing, A = 0, Dst = (byte)relationshipSetup.SocialBondTypeId },
                new() { Op = (ushort)GraphNodeOp.RelationshipSortByMetric, A = 0, Dst = (byte)relationshipSetup.SocialBondTypeId, Imm = relationshipSetup.SupportMetricId, Flags = 1 },
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 99 },
                new() { Op = (ushort)GraphNodeOp.FanOutApplyEffectDynamic, A = 0 },
            };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);

            That(requests.Count, Is.EqualTo(2));
            That(requests[0].TemplateId, Is.EqualTo(99));
            That(requests[0].Target, Is.EqualTo(allyB));
            That(requests[1].Target, Is.EqualTo(allyA));
        }

        [Test]
        public void GraphOps_RelationshipQueryCanFanOutDispatchEffectsWithPayloadPreset()
        {
            using var world = World.Create();
            var requests = new EffectRequestQueue();
            var presetRegistry = new TargetDispatchPresetRegistry();
            int presetId = presetRegistry.Register("SourceToResolved", new TargetResolverContextMapping
            {
                PayloadSource = ContextSlot.OriginalSource,
                PayloadTarget = ContextSlot.ResolvedEntity,
                PayloadTargetContext = ContextSlot.OriginalTarget,
            });

            RelationshipApiSetup relationshipSetup = CreateRelationshipApi(world, requests, presetRegistry);
            var source = world.Create();
            var anchor = world.Create();
            var allyA = world.Create();
            var allyB = world.Create();

            relationshipSetup.Runtime.SetMetric(source, allyA, relationshipSetup.SocialBondTypeId, relationshipSetup.SupportMetricId, 55, reasonId: 0);
            relationshipSetup.Runtime.SetMetric(source, allyB, relationshipSetup.SocialBondTypeId, relationshipSetup.SupportMetricId, 80, reasonId: 0);

            var state = CreateState(world, relationshipSetup.Api, source, anchor);
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.RelationshipQueryOutgoing, A = 0, Dst = (byte)relationshipSetup.SocialBondTypeId },
                new() { Op = (ushort)GraphNodeOp.RelationshipSortByMetric, A = 0, Dst = (byte)relationshipSetup.SocialBondTypeId, Imm = relationshipSetup.SupportMetricId, Flags = 1 },
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 99 },
                new() { Op = (ushort)GraphNodeOp.FanOutDispatchEffectDynamic, A = 0, Dst = (byte)presetId },
            };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);

            That(requests.Count, Is.EqualTo(2));
            That(requests[0].TemplateId, Is.EqualTo(99));
            That(requests[0].Source, Is.EqualTo(source));
            That(requests[0].Target, Is.EqualTo(allyB));
            That(requests[0].TargetContext, Is.EqualTo(anchor));
            That(requests[1].Source, Is.EqualTo(source));
            That(requests[1].Target, Is.EqualTo(allyA));
            That(requests[1].TargetContext, Is.EqualTo(anchor));
        }

        [Test]
        public void GraphOps_RelationshipRemoveLink_RemovesOnlyRequestedTypedEdge()
        {
            using var world = World.Create();
            RelationshipApiSetup relationshipSetup = CreateRelationshipApi(world);
            var source = world.Create();
            var target = world.Create();

            relationshipSetup.Runtime.EnsureLink(source, target, relationshipSetup.SocialBondTypeId);
            relationshipSetup.Runtime.EnsureLink(source, target, relationshipSetup.HostilityTypeId);

            var state = CreateState(world, relationshipSetup.Api, source, target);
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.RelationshipRemoveLink, A = 0, B = 1, Dst = (byte)relationshipSetup.SocialBondTypeId },
            };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);

            That(relationshipSetup.Runtime.HasLink(source, target, relationshipSetup.SocialBondTypeId), Is.False);
            That(relationshipSetup.Runtime.HasLink(source, target, relationshipSetup.HostilityTypeId), Is.True);
        }

        [Test]
        public void GraphOps_RelationshipAddMetric_AddsDeltaToTypedEdge()
        {
            using var world = World.Create();
            RelationshipApiSetup relationshipSetup = CreateRelationshipApi(world);
            var source = world.Create();
            var target = world.Create();

            relationshipSetup.Runtime.SetMetric(source, target, relationshipSetup.SocialBondTypeId, relationshipSetup.LoyaltyMetricId, 10, reasonId: 0);

            var state = CreateState(world, relationshipSetup.Api, source, target);
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 7 },
                new() { Op = (ushort)GraphNodeOp.RelationshipAddMetric, A = 0, B = 1, C = 0, Imm = relationshipSetup.LoyaltyMetricId, Dst = byte.MaxValue, Flags = (byte)relationshipSetup.SocialBondTypeId },
            };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);

            That(relationshipSetup.Runtime.GetMetric(source, target, relationshipSetup.SocialBondTypeId, relationshipSetup.LoyaltyMetricId), Is.EqualTo(17));
        }

        [Test]
        public void GraphOps_RelationshipQueryIncoming_CollectsTypedSources()
        {
            using var world = World.Create();
            RelationshipApiSetup relationshipSetup = CreateRelationshipApi(world);
            var target = world.Create();
            var sourceA = world.Create();
            var sourceB = world.Create();

            relationshipSetup.Runtime.SetMetric(sourceA, target, relationshipSetup.SocialBondTypeId, relationshipSetup.SupportMetricId, 15, reasonId: 0);
            relationshipSetup.Runtime.SetMetric(sourceB, target, relationshipSetup.SocialBondTypeId, relationshipSetup.SupportMetricId, 30, reasonId: 0);

            var state = CreateState(world, relationshipSetup.Api, target, sourceA);
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.RelationshipQueryIncoming, A = 0, Dst = (byte)relationshipSetup.SocialBondTypeId },
            };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);

            That(state.TargetList.Count, Is.EqualTo(2));
            That(state.TargetList.Span[0] == sourceA || state.TargetList.Span[1] == sourceA, Is.True);
            That(state.TargetList.Span[0] == sourceB || state.TargetList.Span[1] == sourceB, Is.True);
        }

        [Test]
        public void GraphOps_RelationshipQueryMutual_CollectsDirectedMutualCandidates()
        {
            using var world = World.Create();
            RelationshipApiSetup relationshipSetup = CreateRelationshipApi(world);
            var first = world.Create();
            var second = world.Create();
            var mutual = world.Create();
            var outsider = world.Create();

            relationshipSetup.Runtime.SetMetric(first, mutual, relationshipSetup.SocialBondTypeId, relationshipSetup.SupportMetricId, 20, reasonId: 0);
            relationshipSetup.Runtime.SetMetric(mutual, second, relationshipSetup.SocialBondTypeId, relationshipSetup.SupportMetricId, 35, reasonId: 0);
            relationshipSetup.Runtime.SetMetric(second, mutual, relationshipSetup.SocialBondTypeId, relationshipSetup.SupportMetricId, 40, reasonId: 0);
            relationshipSetup.Runtime.SetMetric(first, outsider, relationshipSetup.SocialBondTypeId, relationshipSetup.SupportMetricId, 10, reasonId: 0);
            relationshipSetup.Runtime.SetMetric(outsider, second, relationshipSetup.SocialBondTypeId, relationshipSetup.SupportMetricId, 10, reasonId: 0);

            var state = CreateState(world, relationshipSetup.Api, first, second);
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.RelationshipQueryMutual, A = 0, B = 1, Dst = (byte)relationshipSetup.SocialBondTypeId },
            };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);

            That(state.TargetList.Count, Is.EqualTo(1));
            That(state.TargetList.Span[0], Is.EqualTo(mutual));
        }

        [Test]
        public void GraphOps_RelationshipQueryBetweenPair_CollectsBothDirections()
        {
            using var world = World.Create();
            RelationshipApiSetup relationshipSetup = CreateRelationshipApi(world);
            var source = world.Create();
            var target = world.Create();

            relationshipSetup.Runtime.SetMetric(source, target, relationshipSetup.SocialBondTypeId, relationshipSetup.SupportMetricId, 10, reasonId: 0);
            relationshipSetup.Runtime.SetMetric(target, source, relationshipSetup.SocialBondTypeId, relationshipSetup.SupportMetricId, 20, reasonId: 0);

            var state = CreateState(world, relationshipSetup.Api, source, target);
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.RelationshipQueryBetweenPair, A = 0, B = 1, Dst = (byte)relationshipSetup.SocialBondTypeId },
            };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);

            That(state.TargetList.Count, Is.EqualTo(2));
            That(state.TargetList.Span[0], Is.EqualTo(target));
            That(state.TargetList.Span[1], Is.EqualTo(source));
        }

        [Test]
        public void GraphOps_FanOutDispatchEffectDynamic_UsesPayloadPresetMapping()
        {
            using var world = World.Create();
            var requests = new EffectRequestQueue();
            var presetRegistry = new TargetDispatchPresetRegistry();
            int presetId = presetRegistry.Register("TargetToResolved", new TargetResolverContextMapping
            {
                PayloadSource = ContextSlot.OriginalTarget,
                PayloadTarget = ContextSlot.ResolvedEntity,
                PayloadTargetContext = ContextSlot.OriginalSource,
            });

            RelationshipApiSetup relationshipSetup = CreateRelationshipApi(world, requests, presetRegistry);
            var caster = world.Create();
            var anchor = world.Create();
            var allyA = world.Create();
            var allyB = world.Create();
            var targetContext = world.Create();

            var state = CreateState(world, relationshipSetup.Api, caster, anchor);
            state.TargetContext = targetContext;
            state.Targets[0] = allyA;
            state.Targets[1] = allyB;
            state.TargetList.SetCount(2);
            state.I[0] = 99;

            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.FanOutDispatchEffectDynamic, A = 0, Dst = (byte)presetId },
            };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);

            That(requests.Count, Is.EqualTo(2));
            That(requests[0].TemplateId, Is.EqualTo(99));
            That(requests[0].Source, Is.EqualTo(anchor));
            That(requests[0].Target, Is.EqualTo(allyA));
            That(requests[0].TargetContext, Is.EqualTo(caster));
            That(requests[1].Source, Is.EqualTo(anchor));
            That(requests[1].Target, Is.EqualTo(allyB));
            That(requests[1].TargetContext, Is.EqualTo(caster));
        }

        [Test]
        public void GraphCompiler_ApplyEffectDynamicAndFanOutDispatchEffectDynamic_Compile()
        {
            var cfg = new GraphConfig
            {
                Id = "Test.DynamicDispatch",
                Kind = "Effect",
                Entry = "target",
                Nodes =
                {
                    new GraphNodeConfig { Id = "target", Op = "LoadExplicitTarget", Next = "effectId" },
                    new GraphNodeConfig { Id = "effectId", Op = "ConstInt", IntValue = 77, Next = "apply" },
                    new GraphNodeConfig { Id = "apply", Op = "ApplyEffectDynamic", Inputs = { "target", "effectId" }, Next = "fanout" },
                    new GraphNodeConfig { Id = "fanout", Op = "FanOutDispatchEffectDynamic", PayloadPreset = "SourceToResolved", Inputs = { "effectId" } },
                }
            };

            var (pkg, diags) = GraphCompiler.Compile(cfg);

            That(diags, Is.Empty);
            That(pkg.HasValue, Is.True);
            That((GraphNodeOp)pkg!.Value.Program[2].Op, Is.EqualTo(GraphNodeOp.ApplyEffectDynamic));
            That((GraphNodeOp)pkg.Value.Program[3].Op, Is.EqualTo(GraphNodeOp.FanOutDispatchEffectDynamic));
        }

        [Test]
        public void GraphCompiler_FanOutDispatchEffectDynamic_RequiresPayloadPreset()
        {
            var cfg = new GraphConfig
            {
                Id = "Test.FanOutDispatchMissingPreset",
                Kind = "Effect",
                Entry = "effectId",
                Nodes =
                {
                    new GraphNodeConfig { Id = "effectId", Op = "ConstInt", IntValue = 77, Next = "fanout" },
                    new GraphNodeConfig { Id = "fanout", Op = "FanOutDispatchEffectDynamic", Inputs = { "effectId" } },
                }
            };

            var (pkg, diags) = GraphCompiler.Compile(cfg);

            That(pkg, Is.Null);
            That(diags.Exists(d => d.Severity == GraphDiagnosticSeverity.Error && d.Message.Contains("payloadPreset", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void GraphCompiler_RelationshipMetricOpsRequireExplicitRelationshipType()
        {
            var cfg = new GraphConfig
            {
                Id = "Test.RelationshipMissingType",
                Kind = "Effect",
                Entry = "source",
                Nodes =
                {
                    new GraphNodeConfig { Id = "source", Op = "LoadCaster", Next = "target" },
                    new GraphNodeConfig { Id = "target", Op = "LoadExplicitTarget", Next = "metric" },
                    new GraphNodeConfig { Id = "metric", Op = "RelationshipGetMetric", Metric = "Loyalty", Inputs = { "source", "target" } },
                }
            };

            var (pkg, diags) = GraphCompiler.Compile(cfg);

            That(pkg, Is.Null);
            That(diags.Exists(d => d.Severity == GraphDiagnosticSeverity.Error && d.Message.Contains("relationshipType", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void GraphOps_RelationshipVmRejectsImplicitDefaultTypeId()
        {
            using var world = World.Create();
            RelationshipApiSetup relationshipSetup = CreateRelationshipApi(world);
            var source = world.Create();
            var target = world.Create();

            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.RelationshipEnsureLink, A = 0, B = 1, Dst = byte.MaxValue },
            };

            var ex = Throws<InvalidOperationException>(() =>
                ExecuteProgram(world, relationshipSetup.Api, source, target, program));

            That(ex!.Message, Does.Contain("explicit relationshipType"));
        }

        private static Entity ExecuteAndGetEntity(World world, IGraphRuntimeApi api,
            Entity caster, Entity target, GraphInstruction[] program, int entityReg)
        {
            var f = new float[GraphVmLimits.MaxFloatRegisters];
            var i = new int[GraphVmLimits.MaxIntRegisters];
            var b = new byte[GraphVmLimits.MaxBoolRegisters];
            var e = new Entity[GraphVmLimits.MaxEntityRegisters];
            var targets = new Entity[GraphVmLimits.MaxTargets];
            e[0] = caster;
            e[1] = target;

            var state = new GraphExecutionState
            {
                World = world, Caster = caster, ExplicitTarget = target,
                TargetPosCm = default, Api = api,
                F = f, I = i, B = b, E = e,
                Targets = targets, TargetList = new GraphTargetList(targets),
            };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
            return e[entityReg];
        }

        private static Entity ExecuteAndGetEntityWithTargetContext(World world, IGraphRuntimeApi api,
            Entity caster, Entity target, Entity targetCtx, GraphInstruction[] program, int entityReg)
        {
            var f = new float[GraphVmLimits.MaxFloatRegisters];
            var i = new int[GraphVmLimits.MaxIntRegisters];
            var b = new byte[GraphVmLimits.MaxBoolRegisters];
            var e = new Entity[GraphVmLimits.MaxEntityRegisters];
            var targets = new Entity[GraphVmLimits.MaxTargets];
            e[0] = caster;
            e[1] = target;

            var state = new GraphExecutionState
            {
                World = world, Caster = caster, ExplicitTarget = target,
                TargetPosCm = default, Api = api,
                F = f, I = i, B = b, E = e,
                Targets = targets, TargetList = new GraphTargetList(targets),
                TargetContext = targetCtx,
            };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
            return e[entityReg];
        }

        private static void ExecuteProgram(World world, IGraphRuntimeApi api,
            Entity caster, Entity target, GraphInstruction[] program)
        {
            var f = new float[GraphVmLimits.MaxFloatRegisters];
            var i = new int[GraphVmLimits.MaxIntRegisters];
            var b = new byte[GraphVmLimits.MaxBoolRegisters];
            var e = new Entity[GraphVmLimits.MaxEntityRegisters];
            var targets = new Entity[GraphVmLimits.MaxTargets];
            e[0] = caster;
            e[1] = target;

            var state = new GraphExecutionState
            {
                World = world, Caster = caster, ExplicitTarget = target,
                TargetPosCm = default, Api = api,
                F = f, I = i, B = b, E = e,
                Targets = targets, TargetList = new GraphTargetList(targets),
            };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
        }

        private static GraphExecutionState CreateState(World world, IGraphRuntimeApi api, Entity caster, Entity target)
        {
            var f = new float[GraphVmLimits.MaxFloatRegisters];
            var i = new int[GraphVmLimits.MaxIntRegisters];
            var b = new byte[GraphVmLimits.MaxBoolRegisters];
            var e = new Entity[GraphVmLimits.MaxEntityRegisters];
            var targets = new Entity[GraphVmLimits.MaxTargets];
            e[0] = caster;
            e[1] = target;

            return new GraphExecutionState
            {
                World = world,
                Caster = caster,
                ExplicitTarget = target,
                TargetPosCm = default,
                Api = api,
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                TargetList = new GraphTargetList(targets),
            };
        }

        private static RelationshipApiSetup CreateRelationshipApi(
            World world,
            EffectRequestQueue? effectRequests = null,
            TargetDispatchPresetRegistry? targetDispatchPresets = null)
        {
            var typeRegistry = new RelationshipTypeRegistry();
            var metricRegistry = new RelationshipMetricRegistry();
            var flagRegistry = new RelationshipFlagRegistry();
            var reasonRegistry = new RelationshipReasonRegistry();
            var bandRegistry = new RelationshipBandRegistry();
            var changeBuffer = new RelationshipChangeBuffer();
            var runtime = new RelationshipRuntime(world, typeRegistry, metricRegistry, flagRegistry, bandRegistry, changeBuffer, new RelationshipReverseIndex(world));
            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry(), new GasBudget());

            int socialBondTypeId = typeRegistry.Register("SocialBond");
            int hostilityTypeId = typeRegistry.Register("Hostility");
            int loyaltyMetricId = metricRegistry.Register("Loyalty", -100, 100, 0);
            int supportMetricId = metricRegistry.Register("Support", -100, 100, 0);
            int threatMetricId = metricRegistry.Register("Threat", 0, 200, 0);
            int trustedFlagId = flagRegistry.Register("Trusted");
            var entityQueries = new EntitySetQueryRuntime(world, tagOps, runtime);

            var api = new GasGraphRuntimeApi(
                world,
                effectRequests: effectRequests,
                tagOps: tagOps,
                relationshipRuntime: runtime,
                typeRegistry: typeRegistry,
                metricRegistry: metricRegistry,
                flagRegistry: flagRegistry,
                reasonRegistry: reasonRegistry,
                targetDispatchPresets: targetDispatchPresets,
                entityQueries: entityQueries);

            return new RelationshipApiSetup(api, runtime, socialBondTypeId, hostilityTypeId, loyaltyMetricId, supportMetricId, threatMetricId, trustedFlagId);
        }

        private sealed record RelationshipApiSetup(
            GasGraphRuntimeApi Api,
            RelationshipRuntime Runtime,
            int SocialBondTypeId,
            int HostilityTypeId,
            int LoyaltyMetricId,
            int SupportMetricId,
            int ThreatMetricId,
            int TrustedFlagId);
    }
}
