using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.TagDisplay;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class TagDisplayGraphOpsTests
    {
        private DirtyEntityQueue _dirty = null!;
        private TagOps _tagOps = null!;

        [SetUp]
        public void SetUp()
        {
            TagRegistry.Clear();
            _dirty = new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME);
            _tagOps = new TagOps(_dirty, new TagRuleRegistry());
        }

        [TearDown]
        public void TearDown()
        {
            TagRegistry.Clear();
        }

        [Test]
        public void SelectTagInMask_RequireOne_ReturnsMatchingTagId()
        {
            int idle = TagRegistry.Register("State.Idle");
            int moving = TagRegistry.Register("State.Moving");
            var tables = CreateStateTable(idle, moving, tokenIdle: 11, tokenMoving: 12);

            using World world = World.Create();
            Entity entity = world.Create(new GameplayTagContainer(), new TagCountContainer());
            ref var tags = ref world.Get<GameplayTagContainer>(entity);
            ref var counts = ref world.Get<TagCountContainer>(entity);
            _tagOps.AddTag(ref tags, ref counts, moving);

            var api = new GasGraphRuntimeApi(world, tagOps: _tagOps, tagDisplayTables: tables);
            int tableId = tables.GetTableId("entity.state.display");
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.LoadCaster, Dst = 0 },
                new() { Op = (ushort)GraphNodeOp.SelectTagInMask, Dst = 1, A = 0, Imm = tableId, Flags = (byte)TagSelectPolicy.RequireOne },
            };

            Assert.That(ExecuteInt(world, api, entity, program, intReg: 1), Is.EqualTo(moving));
        }

        [Test]
        public void SelectTagInMask_RequireOne_ThrowsWhenMultipleMatch()
        {
            int idle = TagRegistry.Register("State.Idle");
            int moving = TagRegistry.Register("State.Moving");
            var tables = CreateStateTable(idle, moving, tokenIdle: 11, tokenMoving: 12);

            using World world = World.Create();
            Entity entity = world.Create(new GameplayTagContainer(), new TagCountContainer());
            ref var tags = ref world.Get<GameplayTagContainer>(entity);
            ref var counts = ref world.Get<TagCountContainer>(entity);
            _tagOps.AddTag(ref tags, ref counts, idle);
            _tagOps.AddTag(ref tags, ref counts, moving);

            var api = new GasGraphRuntimeApi(world, tagOps: _tagOps, tagDisplayTables: tables);
            int tableId = tables.GetTableId("entity.state.display");
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.LoadCaster, Dst = 0 },
                new() { Op = (ushort)GraphNodeOp.SelectTagInMask, Dst = 1, A = 0, Imm = tableId, Flags = (byte)TagSelectPolicy.RequireOne },
            };

            Assert.That(
                () => ExecuteInt(world, api, entity, program, intReg: 1),
                Throws.InvalidOperationException.With.Message.Contains("TagSelectRequireOneFailed"));
        }

        [Test]
        public void LookupTagDisplayToken_ReturnsMappedToken()
        {
            int idle = TagRegistry.Register("State.Idle");
            int moving = TagRegistry.Register("State.Moving");
            var tables = CreateStateTable(idle, moving, tokenIdle: 11, tokenMoving: 12);

            using World world = World.Create();
            Entity entity = world.Create();
            var api = new GasGraphRuntimeApi(world, tagOps: _tagOps, tagDisplayTables: tables);
            int tableId = tables.GetTableId("entity.state.display");
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = moving },
                new() { Op = (ushort)GraphNodeOp.LookupTagDisplayToken, Dst = 1, A = 0, Imm = tableId },
            };

            Assert.That(ExecuteInt(world, api, entity, program, intReg: 1), Is.EqualTo(12));
        }

        [Test]
        public void LookupTagDisplayToken_MissingMapping_Throws()
        {
            int idle = TagRegistry.Register("State.Idle");
            int moving = TagRegistry.Register("State.Moving");
            int attacking = TagRegistry.Register("State.Attacking");
            var tables = CreateStateTable(idle, moving, tokenIdle: 11, tokenMoving: 12);
            // attacking is not in table entries (and not in mask)

            using World world = World.Create();
            Entity entity = world.Create();
            var api = new GasGraphRuntimeApi(world, tagOps: _tagOps, tagDisplayTables: tables);
            int tableId = tables.GetTableId("entity.state.display");
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = attacking },
                new() { Op = (ushort)GraphNodeOp.LookupTagDisplayToken, Dst = 1, A = 0, Imm = tableId },
            };

            Assert.That(
                () => ExecuteInt(world, api, entity, program, intReg: 1),
                Throws.InvalidOperationException.With.Message.Contains(TagDisplayTableRegistry.MappingMissingError));
        }

        [Test]
        public void GraphNodeOpParser_AuthoringSugar_MapsToL0()
        {
            Assert.That(GraphNodeOpParser.TryParse("ReadGameplayTag", out GraphNodeOp select), Is.True);
            Assert.That(select, Is.EqualTo(GraphNodeOp.SelectTagInMask));
            Assert.That(GraphNodeOpParser.TryParse("LookupTagDisplayText", out GraphNodeOp lookup), Is.True);
            Assert.That(lookup, Is.EqualTo(GraphNodeOp.LookupTagDisplayToken));
        }

        [Test]
        public void SelectThenLookup_Chain_YieldsTokenId()
        {
            int idle = TagRegistry.Register("State.Idle");
            int moving = TagRegistry.Register("State.Moving");
            var tables = CreateStateTable(idle, moving, tokenIdle: 11, tokenMoving: 12);

            using World world = World.Create();
            Entity entity = world.Create(new GameplayTagContainer(), new TagCountContainer());
            ref var tags = ref world.Get<GameplayTagContainer>(entity);
            ref var counts = ref world.Get<TagCountContainer>(entity);
            _tagOps.AddTag(ref tags, ref counts, idle);

            var api = new GasGraphRuntimeApi(world, tagOps: _tagOps, tagDisplayTables: tables);
            int tableId = tables.GetTableId("entity.state.display");
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.LoadCaster, Dst = 0 },
                new() { Op = (ushort)GraphNodeOp.SelectTagInMask, Dst = 2, A = 0, Imm = tableId, Flags = (byte)TagSelectPolicy.RequireOne },
                new() { Op = (ushort)GraphNodeOp.LookupTagDisplayToken, Dst = 3, A = 2, Imm = tableId },
            };

            Assert.That(ExecuteInt(world, api, entity, program, intReg: 3), Is.EqualTo(11));
        }

        private static TagDisplayTableRegistry CreateStateTable(int idle, int moving, int tokenIdle, int tokenMoving)
        {
            var mask = new GameplayTagContainer();
            mask.AddTag(idle);
            mask.AddTag(moving);
            var tables = new TagDisplayTableRegistry();
            tables.RegisterTable(
                "entity.state.display",
                in mask,
                new (int, int)[] { (idle, tokenIdle), (moving, tokenMoving) });
            tables.Freeze();
            return tables;
        }

        private static int ExecuteInt(
            World world,
            IGraphRuntimeApi api,
            Entity caster,
            GraphInstruction[] program,
            int intReg)
        {
            var f = new float[GraphVmLimits.MaxFloatRegisters];
            var i = new int[GraphVmLimits.MaxIntRegisters];
            var e = new Entity[GraphVmLimits.MaxEntityRegisters];
            var b = new byte[GraphVmLimits.MaxBoolRegisters];
            var targets = new Entity[GraphVmLimits.MaxTargets];
            e[0] = caster;
            e[1] = caster;
            var state = new GraphExecutionState
            {
                World = world,
                Api = api,
                Caster = caster,
                ExplicitTarget = caster,
                F = f,
                I = i,
                E = e,
                B = b,
                Targets = targets,
                TargetList = new GraphTargetList(targets),
                CallStack = new int[GraphVmLimits.MaxCallStackDepth],
                CallStackCount = 0,
            };
            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
            return state.I[intReg];
        }
    }
}
