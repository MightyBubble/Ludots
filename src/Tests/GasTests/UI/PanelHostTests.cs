using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.UI.PanelHosting;
using Ludots.Core.UI.PanelProjection;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.UI
{
    /// <summary>
    /// Panel instantiation (CreatePanel/DestroyPanel ops) and the refresh model:
    /// realtime variables move on RefreshRealtime, everything else only on Refresh(handle).
    /// Graph op and direct code terminate at the same PanelHost API.
    /// </summary>
    [TestFixture]
    public sealed class PanelHostTests
    {
        private const string TemplateId = "tests.panel.host";
        private const string AnchorId = "tests.anchor.bottom_right";

        private const string TemplateJson = """
        {
          "id": "tests.panel.host",
          "variables": [
            { "name": "hp", "kind": "Float", "realtime": true,
              "source": { "sourceKind": "SingleAttribute", "attributeId": "tests.attr.hp" } },
            { "name": "attack", "kind": "Float",
              "source": { "sourceKind": "SingleAttribute", "attributeId": "tests.attr.attack" } }
          ],
          "binds": [
            { "control": "lbl.hp", "variable": "hp" },
            { "control": "lbl.attack", "variable": "attack" }
          ]
        }
        """;

        private World _world = null!;
        private Entity _caster;
        private Entity _target;
        private int _hpId;
        private int _attackId;
        private PanelTemplateRegistry _templates = null!;
        private PanelHost _host = null!;
        private GasGraphRuntimeApi _api = null!;
        private GraphProgramRegistry _programs = null!;

        [SetUp]
        public void SetUp()
        {
            AttributeRegistry.Clear();
            ConfigKeyRegistry.Clear();
            GraphIdRegistry.Clear();
            _hpId = AttributeRegistry.Register("tests.attr.hp");
            _attackId = AttributeRegistry.Register("tests.attr.attack");

            _world = World.Create();
            _caster = CreateSoldier(hp: 87f, attack: 12f);
            _target = CreateSoldier(hp: 41f, attack: 5f);

            _templates = new PanelTemplateRegistry();
            _templates.Register(PanelTemplateLoader.Load(TemplateJson));
            _templates.Freeze();

            _host = new PanelHost(_templates, new PanelProjectionReader(_world));
            _api = new GasGraphRuntimeApi(_world);
            _api.BindPanelHost(_host);
            _programs = new GraphProgramRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
            AttributeRegistry.Clear();
            ConfigKeyRegistry.Clear();
            GraphIdRegistry.Clear();
        }

        private Entity CreateSoldier(float hp, float attack)
        {
            Entity entity = _world.Create();
            _world.Add(entity, new AttributeBuffer());
            ref AttributeBuffer buffer = ref _world.Get<AttributeBuffer>(entity);
            buffer.SetBase(_hpId, hp);
            buffer.SetBase(_attackId, attack);
            return entity;
        }

        [Test]
        public void Instantiate_DirectApi_EvaluatesImmediately()
        {
            PanelInstanceHandle handle = _host.Instantiate(TemplateId, AnchorId, _caster);

            Assert.That(handle.IsValid, Is.True);
            Assert.That(_host.TryGetAnchor(handle, out string anchor), Is.True);
            Assert.That(anchor, Is.EqualTo(AnchorId));
            Assert.That(_host.TryGetScope(handle, out Entity scope), Is.True);
            Assert.That(scope, Is.EqualTo(_caster));
            Assert.That(_host.TryGetValues(handle, out PanelVariableSet values), Is.True);
            Assert.That(values.Get("hp"), Is.EqualTo(87f));
            Assert.That(values.Get("attack"), Is.EqualTo(12f));
        }

        [Test]
        public void Instantiate_UnknownTemplate_FailsNamingId()
        {
            Assert.That(
                () => _host.Instantiate("tests.panel.ghost", AnchorId, _caster),
                Throws.InvalidOperationException.With.Message.Contains("tests.panel.ghost"));
        }

        [Test]
        public void Instantiate_EmptyAnchor_Rejected()
        {
            Assert.That(
                () => _host.Instantiate(TemplateId, "  ", _caster),
                Throws.ArgumentException);
        }

        [Test]
        public void Instantiate_BrokenBinding_FailsAtCreationNamingVariable()
        {
            Entity bare = _world.Create();
            Assert.That(
                () => _host.Instantiate(TemplateId, AnchorId, bare),
                Throws.InvalidOperationException.With.Message.Contains("hp"));
        }

        [Test]
        public void CreatePanelOp_MatchesDirectApi_StateForState()
        {
            RunScript(CreatePanelProgram(scopeRegister: byte.MaxValue), _caster, _caster,
                symbols: new[] { TemplateId, AnchorId });

            PanelInstanceHandle direct = _host.Instantiate(TemplateId, AnchorId, _caster);

            Assert.That(_host.Count, Is.EqualTo(2));
            Assert.That(FindByScope(_caster, out PanelInstanceHandle viaOp), Is.True);
            Assert.That(_host.TryGetValues(viaOp, out PanelVariableSet opValues), Is.True);
            Assert.That(_host.TryGetValues(direct, out PanelVariableSet directValues), Is.True);
            Assert.That(opValues.Get("hp"), Is.EqualTo(directValues.Get("hp")));
            Assert.That(opValues.Get("attack"), Is.EqualTo(directValues.Get("attack")));
            Assert.That(opValues.Revision, Is.EqualTo(directValues.Revision));
            Assert.That(_host.TryGetAnchor(viaOp, out string opAnchor), Is.True);
            Assert.That(opAnchor, Is.EqualTo(AnchorId));
        }

        [Test]
        public void CreatePanelOp_ExplicitScopeRegister_WinsOverCaster()
        {
            // LoadExplicitTarget into E1, then CreatePanel with A=1 → scope is target, not caster.
            RunScript(new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.CreatePanel, Imm = 0, Dst = 1, A = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            }, _caster, _target, symbols: new[] { TemplateId, AnchorId });

            Assert.That(FindByScope(_target, out PanelInstanceHandle handle), Is.True);
            Assert.That(_host.TryGetValues(handle, out PanelVariableSet values), Is.True);
            Assert.That(values.Get("hp"), Is.EqualTo(41f));
        }

        [Test]
        public void DestroyPanelOp_ScopedThenUnscoped()
        {
            RunScript(CreatePanelProgram(scopeRegister: byte.MaxValue), _caster, _caster,
                symbols: new[] { TemplateId, AnchorId });
            RunScript(CreatePanelProgram(scopeRegister: byte.MaxValue), _target, _target,
                symbols: new[] { TemplateId, AnchorId });
            Assert.That(_host.Count, Is.EqualTo(2));

            // Scoped destroy: only the target's instance goes.
            RunScript(new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.DestroyPanel, Imm = 0, A = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            }, _caster, _target, symbols: new[] { TemplateId });
            Assert.That(_host.Count, Is.EqualTo(1));
            Assert.That(FindByScope(_caster, out PanelInstanceHandle survivor), Is.True);

            // Unscoped destroy: everything of the template goes; the surviving handle goes stale.
            RunScript(DestroyPanelProgram(), _caster, _caster, symbols: new[] { TemplateId });
            Assert.That(_host.Count, Is.EqualTo(0));
            Assert.That(() => _host.Refresh(survivor), Throws.InvalidOperationException);
        }

        [Test]
        public void RefreshRealtime_TouchesOnlyRealtimeVariables()
        {
            PanelInstanceHandle handle = _host.Instantiate(TemplateId, AnchorId, _caster);

            ref AttributeBuffer buffer = ref _world.Get<AttributeBuffer>(_caster);
            buffer.SetBase(_hpId, 50f);
            buffer.SetBase(_attackId, 99f);

            Assert.That(_host.RefreshRealtime(), Is.EqualTo(1));
            Assert.That(_host.TryGetValues(handle, out PanelVariableSet values), Is.True);
            Assert.That(values.Get("hp"), Is.EqualTo(50f), "realtime variable follows RefreshRealtime");
            Assert.That(values.Get("attack"), Is.EqualTo(12f), "non-realtime variable stays put until manual refresh");

            PanelVariableSet full = _host.Refresh(handle);
            Assert.That(full.Get("attack"), Is.EqualTo(99f));
        }

        [Test]
        public void RefreshRealtime_NoChange_ReturnsZero()
        {
            _host.Instantiate(TemplateId, AnchorId, _caster);
            Assert.That(_host.RefreshRealtime(), Is.EqualTo(0));
        }

        [Test]
        public void RefreshRealtime_DeadScope_AutoCollectsInstance()
        {
            PanelInstanceHandle handle = _host.Instantiate(TemplateId, AnchorId, _caster);
            _world.Destroy(_caster);

            Assert.That(_host.RefreshRealtime(), Is.EqualTo(0));
            Assert.That(_host.AutoCollectedLastRefresh, Is.EqualTo(1));
            Assert.That(_host.Count, Is.EqualTo(0));
            Assert.That(_host.TryGetValues(handle, out _), Is.False, "collected instance handle is stale");
        }

        [Test]
        public void RefreshRealtime_TemplateWithoutRealtimeVariables_IsSkipped()
        {
            const string staticTemplateJson = """
            {
              "id": "tests.panel.static",
              "variables": [
                { "name": "attack", "kind": "Float",
                  "source": { "sourceKind": "SingleAttribute", "attributeId": "tests.attr.attack" } }
              ]
            }
            """;
            var templates = new PanelTemplateRegistry();
            templates.Register(PanelTemplateLoader.Load(staticTemplateJson));
            templates.Freeze();
            var host = new PanelHost(templates, new PanelProjectionReader(_world));
            host.Instantiate("tests.panel.static", AnchorId, _caster);

            ref AttributeBuffer buffer = ref _world.Get<AttributeBuffer>(_caster);
            buffer.SetBase(_attackId, 77f);

            Assert.That(host.RefreshRealtime(), Is.EqualTo(0));
            Assert.That(host.TryGetValues(
                FindSingle(host), out PanelVariableSet values), Is.True);
            Assert.That(values.Get("attack"), Is.EqualTo(12f));
        }

        [Test]
        public void RefreshRealtime_DeadScopeWithoutRealtimeVariables_AutoCollectsInstance()
        {
            const string staticTemplateJson = """
            {
              "id": "tests.panel.static.dead",
              "variables": [
                { "name": "attack", "kind": "Float",
                  "source": { "sourceKind": "SingleAttribute", "attributeId": "tests.attr.attack" } }
              ]
            }
            """;
            var templates = new PanelTemplateRegistry();
            templates.Register(PanelTemplateLoader.Load(staticTemplateJson));
            templates.Freeze();
            var host = new PanelHost(templates, new PanelProjectionReader(_world));
            host.Instantiate("tests.panel.static.dead", AnchorId, _caster);

            _world.Destroy(_caster);

            Assert.That(host.RefreshRealtime(), Is.Zero);
            Assert.That(host.AutoCollectedLastRefresh, Is.EqualTo(1));
            Assert.That(host.Count, Is.Zero);
        }

        [Test]
        public void DisposeMapScoped_DisposesOnlyMatchingMapInstances()
        {
            MapId firstMap = new("first");
            MapId secondMap = new("second");
            _world.Add(_caster, new MapEntity { MapId = firstMap });
            _world.Add(_target, new MapEntity { MapId = secondMap });
            _host.Instantiate(TemplateId, AnchorId, _caster);
            _host.Instantiate(TemplateId, AnchorId, _target);

            Assert.That(_host.DisposeMapScoped(firstMap), Is.EqualTo(1));
            Assert.That(_host.Count, Is.EqualTo(1));
            Assert.That(_host.SnapshotInstances()[0].Scope, Is.EqualTo(_target));
        }

        [Test]
        public void Loader_RealtimeFlag_ParsesAndValidates()
        {
            PanelTemplate template = PanelTemplateLoader.Load(TemplateJson);
            Assert.That(template.Variables[0].Realtime, Is.True);
            Assert.That(template.Variables[1].Realtime, Is.False);

            const string badJson = """
            {
              "id": "tests.panel.bad_realtime",
              "variables": [
                { "name": "hp", "kind": "Float", "realtime": "yes",
                  "source": { "sourceKind": "SingleAttribute", "attributeId": "tests.attr.hp" } }
              ]
            }
            """;
            Assert.That(
                () => PanelTemplateLoader.Load(badJson),
                Throws.InvalidOperationException.With.Message.Contains("realtime"));
        }

        [Test]
        public void CatalogLoader_LoadsTemplatesFromConfigPipeline()
        {
            string tempRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Ludots_PanelTemplates", Guid.NewGuid().ToString("N"));
            try
            {
                string dir = System.IO.Path.Combine(tempRoot, "Panels");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "panel_templates.json"), "[" + TemplateJson + "]");

                var vfs = new Ludots.Core.Modding.VirtualFileSystem();
                vfs.Mount("Core", tempRoot);
                var modLoader = new Ludots.Core.Modding.ModLoader(vfs, new Ludots.Core.Scripting.FunctionRegistry(), new Ludots.Core.Scripting.TriggerManager());
                var pipeline = new Ludots.Core.Config.ConfigPipeline(vfs, modLoader);
                var catalog = new Ludots.Core.Config.ConfigCatalog();
                catalog.Add(new Ludots.Core.Config.ConfigCatalogEntry(
                    PanelTemplateCatalogLoader.ConfigPath,
                    Ludots.Core.Config.ConfigMergePolicy.ArrayById,
                    "id"));

                PanelTemplateRegistry registry = new PanelTemplateCatalogLoader(pipeline).Load(catalog);
                Assert.That(registry.Count, Is.EqualTo(1));
                Assert.That(registry.Require(TemplateId).Variables[0].Realtime, Is.True);
            }
            finally
            {
                if (System.IO.Directory.Exists(tempRoot))
                {
                    System.IO.Directory.Delete(tempRoot, recursive: true);
                }
            }
        }

        // ── helpers ──

        private static GraphInstruction[] CreatePanelProgram(byte scopeRegister) => new[]
        {
            new GraphInstruction { Op = (ushort)GraphNodeOp.CreatePanel, Imm = 0, Dst = 1, A = scopeRegister },
            new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 1 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
        };

        private static GraphInstruction[] DestroyPanelProgram() => new[]
        {
            new GraphInstruction { Op = (ushort)GraphNodeOp.DestroyPanel, Imm = 0, A = byte.MaxValue },
            new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 1 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
        };

        private void RunScript(GraphInstruction[] program, Entity caster, Entity explicitTarget, string[] symbols)
        {
            int graphId = GraphIdRegistry.Register("tests.script." + Guid.NewGuid().ToString("N"));
            _programs.Register(graphId, program, GraphKind.Script);
            GraphProgramSymbolPatcher.Patch(symbols, program, new ThrowingResolver());

            int[] ints = new int[GraphVmLimits.MaxIntRegisters];
            byte[] bools = new byte[GraphVmLimits.MaxBoolRegisters];
            int[] callStack = new int[GraphVmLimits.MaxCallStackDepth];
            var cursor = new GraphExecutionCursor();
            GraphInstruction[] registered = _programs.RequireProgramArray(graphId, GraphKind.Script, "panel host test");
            GraphExecutor.ExecuteResolvedRegisteredScriptSlice(
                _programs, registered, ints, bools, callStack, ref cursor, 64, _world, caster, explicitTarget, _api);
        }

        private bool FindByScope(Entity scope, out PanelInstanceHandle handle)
        {
            foreach (PanelHostInstanceInfo info in _host.SnapshotInstances())
            {
                if (info.Scope == scope)
                {
                    handle = info.Handle;
                    return true;
                }
            }

            handle = PanelInstanceHandle.Invalid;
            return false;
        }

        private static PanelInstanceHandle FindSingle(PanelHost host)
        {
            IReadOnlyList<PanelHostInstanceInfo> instances = host.SnapshotInstances();
            if (instances.Count == 1)
            {
                return instances[0].Handle;
            }

            throw new InvalidOperationException($"Expected exactly one live panel instance, found {instances.Count}.");
        }

        private sealed class ThrowingResolver : IGraphSymbolResolver
        {
            public int ResolveTag(string name) => throw new InvalidOperationException($"Unexpected tag '{name}'.");
            public int ResolveAttribute(string name) => throw new InvalidOperationException($"Unexpected attribute '{name}'.");
            public int ResolveEffectTemplate(string name) => throw new InvalidOperationException($"Unexpected effect template '{name}'.");
            public int ResolveRelationshipType(string name) => throw new InvalidOperationException($"Unexpected relationship type '{name}'.");
            public int ResolveRelationshipMetric(string name) => throw new InvalidOperationException($"Unexpected relationship metric '{name}'.");
            public int ResolveRelationshipFlag(string name) => throw new InvalidOperationException($"Unexpected relationship flag '{name}'.");
            public int ResolveRelationshipReason(string name) => throw new InvalidOperationException($"Unexpected relationship reason '{name}'.");
            public int ResolveTargetDispatchPreset(string name) => throw new InvalidOperationException($"Unexpected dispatch preset '{name}'.");
            public int ResolveEntityTemplate(string name) => throw new InvalidOperationException($"Unexpected entity template '{name}'.");
        }
    }
}
