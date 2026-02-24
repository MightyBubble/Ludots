using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Tests covering critical gaps identified in the GAS Effect system architecture audit.
    /// Each section targets a specific blind spot in the existing test suite.
    /// </summary>
    [TestFixture]
    public class AuditCoverageTests
    {
        // ════════════════════════════════════════════════════════════════════
        //  Section 1: EffectPhaseExecutor — Builtin Handler Path
        //  (Previously zero coverage: all existing tests used empty PresetTypeRegistry)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void PhaseExecutor_BuiltinPath_ApplyModifiers_AppliesViaPresetType()
        {
            using var world = World.Create();

            // Set up registries with REAL PresetTypeRegistry (not empty)
            var presetTypes = new PresetTypeRegistry();
            var def = new PresetTypeDefinition
            {
                Type = EffectPresetType.InstantDamage,
                Components = ComponentFlags.ModifierParams,
                ActivePhases = PhaseFlags.InstantCore,
                AllowedLifetimes = LifetimeFlags.InstantOnly,
            };
            def.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Builtin(BuiltinHandlerId.ApplyModifiers);
            presetTypes.Register(in def);

            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);

            var templates = new EffectTemplateRegistry();
            var mods = default(EffectModifiers);
            mods.Add(attrId: 0, ModifierOp.Add, -25f);
            templates.Register(1, new EffectTemplateData
            {
                TagId = 1,
                LifetimeKind = EffectLifetimeKind.Instant,
                Modifiers = mods,
            });

            var programs = new GraphProgramRegistry();
            var handlers = GasGraphOpHandlerTable.Instance;

            // Use the NEW constructor that takes PresetTypeRegistry + BuiltinHandlerRegistry
            var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, handlers, templates);
            var api = new GasGraphRuntimeApi(world, null, null, null);

            var caster = world.Create();
            var target = world.Create(new AttributeBuffer());
            world.Get<AttributeBuffer>(target).SetCurrent(0, 100f);

            var behavior = new EffectPhaseGraphBindings();

            executor.ExecutePhase(world, api, caster, target, default, default,
                EffectPhaseId.OnApply, in behavior, EffectPresetType.InstantDamage,
                effectTagId: 1, effectTemplateId: 1);

            float hp = world.Get<AttributeBuffer>(target).GetCurrent(0);
            That(hp, Is.EqualTo(75f), "ApplyModifiers via Builtin handler path should reduce HP by 25");
        }

        [Test]
        public void PhaseExecutor_BuiltinPath_TemplateMissing_ThrowsInvalidOperation()
        {
            using var world = World.Create();

            var presetTypes = new PresetTypeRegistry();
            var def = new PresetTypeDefinition
            {
                Type = EffectPresetType.InstantDamage,
                Components = ComponentFlags.ModifierParams,
                ActivePhases = PhaseFlags.InstantCore,
                AllowedLifetimes = LifetimeFlags.InstantOnly,
            };
            def.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Builtin(BuiltinHandlerId.ApplyModifiers);
            presetTypes.Register(in def);

            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);

            // Empty template registry — template ID 999 does not exist
            var templates = new EffectTemplateRegistry();
            var programs = new GraphProgramRegistry();
            var handlers = GasGraphOpHandlerTable.Instance;

            var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, handlers, templates);
            var api = new GasGraphRuntimeApi(world, null, null, null);

            var caster = world.Create();
            var target = world.Create(new AttributeBuffer());
            world.Get<AttributeBuffer>(target).SetCurrent(0, 100f);

            var behavior = new EffectPhaseGraphBindings();

            // fail-fast: missing template must throw, not silently skip
            Assert.Throws<InvalidOperationException>(() =>
                executor.ExecutePhase(world, api, caster, target, default, default,
                    EffectPhaseId.OnApply, in behavior, EffectPresetType.InstantDamage,
                    effectTagId: 1, effectTemplateId: 999));

            // HP unchanged — exception prevented the handler from running
            float hp = world.Get<AttributeBuffer>(target).GetCurrent(0);
            That(hp, Is.EqualTo(100f), "Exception prevented handler execution, HP unchanged");
        }

        // ════════════════════════════════════════════════════════════════════
        //  Section 2: EffectProposalProcessingSystem — ResetSlice
        //  (Previously: only behavior test that happened to pass even with no-op)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void ProposalProcessing_ResetSlice_ClearsWindowPhaseToNone()
        {
            var world = World.Create();
            try
            {
                int tplInstant = 1001;
                var templates = new EffectTemplateRegistry();
                templates.Register(tplInstant, new EffectTemplateData
                {
                    TagId = 1,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.Step,
                    ParticipatesInResponse = false,
                    Modifiers = default,
                });

                var budget = new GasBudget();
                var queue = new EffectRequestQueue();
                var target = world.Create(new AttributeBuffer());

                queue.Publish(new EffectRequest
                {
                    RootId = 1,
                    Source = default,
                    Target = target,
                    TargetContext = default,
                    TemplateId = tplInstant,
                });

                var sys = new EffectProposalProcessingSystem(world, queue, budget, templates, inputRequests: null, chainOrders: null)
                {
                    MaxWorkUnitsPerSlice = 1 // Force partial processing
                };

                // Begin processing — should enter active state
                sys.UpdateSlice(dt: 1f, timeBudgetMs: int.MaxValue);

                // DebugWindowPhase > 0 means the system is in an active window phase
                byte phaseBefore = sys.DebugWindowPhase;

                // ResetSlice must clear the state
                sys.ResetSlice();

                byte phaseAfter = sys.DebugWindowPhase;
                That(phaseAfter, Is.EqualTo(0), "After ResetSlice, WindowPhase must be None (0)");
            }
            finally
            {
                world.Dispose();
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  Section 3: Component Boundary Conditions
        //  (Previously: no out-of-bounds / overflow tests)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public unsafe void EffectGrantedTags_Get_OutOfBounds_ReturnsDefault()
        {
            var tags = new EffectGrantedTags();
            tags.Add(new TagContribution { TagId = 42, Formula = TagContributionFormula.Fixed, Amount = 1 });

            // Valid index
            var valid = tags.Get(0);
            That(valid.TagId, Is.EqualTo(42));

            // Out of bounds: Count = 1, so index 1 should return default
            var oob = tags.Get(1);
            That(oob.TagId, Is.EqualTo(0), "Out-of-bounds Get should return default (TagId=0)");

            // Negative index
            var neg = tags.Get(-1);
            That(neg.TagId, Is.EqualTo(0), "Negative index Get should return default");

            // Way out of bounds
            var far = tags.Get(EffectGrantedTags.MAX_GRANTS + 10);
            That(far.TagId, Is.EqualTo(0), "Far out-of-bounds Get should return default");
        }

        [Test]
        public unsafe void EffectGrantedTags_Add_Overflow_ReturnsFalseAndIsCapped()
        {
            var tags = new EffectGrantedTags();
            int successCount = 0;
            bool lastOverflowResult = true;
            for (int i = 0; i < EffectGrantedTags.MAX_GRANTS + 5; i++)
            {
                bool ok = tags.Add(new TagContribution { TagId = 100 + i, Formula = TagContributionFormula.Fixed, Amount = 1 });
                if (ok) successCount++;
                if (i >= EffectGrantedTags.MAX_GRANTS) lastOverflowResult = ok;
            }

            That(tags.Count, Is.EqualTo(EffectGrantedTags.MAX_GRANTS),
                "Count must not exceed MAX_GRANTS even after overflow adds");
            That(successCount, Is.EqualTo(EffectGrantedTags.MAX_GRANTS),
                "Only MAX_GRANTS adds should succeed");
            That(lastOverflowResult, Is.False,
                "Overflow add must return false");
        }

        [Test]
        public unsafe void ActiveEffectContainer_Add_Overflow_ReturnsFalseAndIsCapped()
        {
            using var world = World.Create();
            var container = new ActiveEffectContainer();

            // Fill to capacity
            for (int i = 0; i < ActiveEffectContainer.CAPACITY; i++)
            {
                var entity = world.Create();
                bool ok = container.Add(entity);
                That(ok, Is.True, $"Add at index {i} should succeed");
            }
            That(container.Count, Is.EqualTo(ActiveEffectContainer.CAPACITY));

            // One more — should return false
            var overflow = world.Create();
            bool overflowResult = container.Add(overflow);
            That(overflowResult, Is.False, "Overflow add must return false");
            That(container.Count, Is.EqualTo(ActiveEffectContainer.CAPACITY),
                "Count must not exceed CAPACITY after overflow add");
        }

        [Test]
        public unsafe void EffectModifiers_Add_Overflow_ReturnsFalseAndIsCapped()
        {
            var mods = new EffectModifiers();
            int successCount = 0;
            for (int i = 0; i < EffectModifiers.CAPACITY + 3; i++)
            {
                if (mods.Add(attrId: i, ModifierOp.Add, (float)i))
                    successCount++;
            }

            That(mods.Count, Is.EqualTo(EffectModifiers.CAPACITY),
                "Count must not exceed CAPACITY after overflow adds");
            That(successCount, Is.EqualTo(EffectModifiers.CAPACITY),
                "Only CAPACITY adds should succeed");

            // Verify last valid entry is the 8th (index 7), not corrupted
            var last = mods.Get(EffectModifiers.CAPACITY - 1);
            That(last.AttributeId, Is.EqualTo(EffectModifiers.CAPACITY - 1));
        }

        [Test]
        public unsafe void EffectModifiers_Get_OutOfBounds_ReturnsDefault()
        {
            var mods = new EffectModifiers();
            mods.Add(attrId: 1, ModifierOp.Add, 10f);

            var valid = mods.Get(0);
            That(valid.AttributeId, Is.EqualTo(1));

            var oob = mods.Get(1);
            That(oob.AttributeId, Is.EqualTo(0), "Out-of-bounds Get should return default");

            var neg = mods.Get(-1);
            That(neg.AttributeId, Is.EqualTo(0), "Negative index Get should return default");
        }

        [Test]
        public unsafe void EffectConfigParams_Overflow_IsCapped()
        {
            var p = new EffectConfigParams();
            int added = 0;
            for (int i = 0; i < EffectConfigParams.MAX_PARAMS + 5; i++)
            {
                if (p.TryAddFloat(keyId: 1000 + i, (float)i))
                    added++;
            }

            That(added, Is.EqualTo(EffectConfigParams.MAX_PARAMS),
                "TryAddFloat should return false after reaching MAX_PARAMS");
            That(p.Count, Is.EqualTo(EffectConfigParams.MAX_PARAMS));
        }

        // ════════════════════════════════════════════════════════════════════
        //  Section 4: EffectPhaseExecutor — PresetType integration
        //  (Previously: all tests used legacy PresetBehaviorRegistry constructor)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void PhaseExecutor_BuiltinApplyForce_WritesAttributesViaPresetType()
        {
            using var world = World.Create();

            EffectParamKeys.Initialize();

            var presetTypes = new PresetTypeRegistry();
            var def = new PresetTypeDefinition
            {
                Type = EffectPresetType.ApplyForce2D,
                Components = ComponentFlags.ForceParams,
                ActivePhases = EffectPhaseId.OnApply.ToFlag(),
                AllowedLifetimes = LifetimeFlags.InstantOnly,
            };
            def.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Builtin(BuiltinHandlerId.ApplyForce);
            presetTypes.Register(in def);

            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);

            int forceXAttrId = 50;
            int forceYAttrId = 51;

            var configParams = new EffectConfigParams();
            configParams.TryAddFloat(EffectParamKeys.ForceXAttribute, 100f);
            configParams.TryAddFloat(EffectParamKeys.ForceYAttribute, -50f);

            var templates = new EffectTemplateRegistry();
            templates.Register(1, new EffectTemplateData
            {
                TagId = 1,
                LifetimeKind = EffectLifetimeKind.Instant,
                PresetType = EffectPresetType.ApplyForce2D,
                PresetAttribute0 = forceXAttrId,
                PresetAttribute1 = forceYAttrId,
                ConfigParams = configParams,
            });

            var programs = new GraphProgramRegistry();
            var handlers = GasGraphOpHandlerTable.Instance;

            var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, handlers, templates);
            var api = new GasGraphRuntimeApi(world, null, null, null);

            var caster = world.Create();
            var target = world.Create(new AttributeBuffer());

            var behavior = new EffectPhaseGraphBindings();

            executor.ExecutePhase(world, api, caster, target, default, default,
                EffectPhaseId.OnApply, in behavior, EffectPresetType.ApplyForce2D,
                effectTagId: 1, effectTemplateId: 1);

            ref var buf = ref world.Get<AttributeBuffer>(target);
            That(buf.GetCurrent(forceXAttrId), Is.EqualTo(100f), "ForceX should be applied");
            That(buf.GetCurrent(forceYAttrId), Is.EqualTo(-50f), "ForceY should be applied");
        }

        // ════════════════════════════════════════════════════════════════════
        //  Section 5: MobaDemoMod Config Chain Smoke Test
        //  (Previously: zero coverage for the entire mod)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void MobaDemoMod_EffectsJson_LoadsAllTemplatesViaVfs()
        {
            string repoRoot = FindRepoRoot();
            string mobaModDir = Path.Combine(repoRoot, "src", "Mods", "MobaDemoMod");
            string coreAssetsDir = Path.Combine(repoRoot, "assets");

            if (!Directory.Exists(mobaModDir))
            {
                Assert.Ignore("MobaDemoMod directory not found.");
                return;
            }

            // Build a VFS + ConfigPipeline that layers Core + MobaDemoMod
            // ConfigPipeline.LoadFromAllSources loads mods via "{modId}:assets/{path}"
            // so we mount at the mod root, not the assets subdirectory.
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", coreAssetsDir);
            vfs.Mount("MobaDemoMod", mobaModDir);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            modLoader.LoadedModIds.Add("MobaDemoMod");
            var pipeline = new ConfigPipeline(vfs, modLoader);

            EffectParamKeys.Initialize();
            EffectTemplateIdRegistry.Clear();

            // Pre-register graph programs referenced by MobaDemoMod effect templates.
            // In production, GraphProgramLoader runs before EffectTemplateLoader.
            Ludots.Core.NodeLibraries.GASGraph.Host.GraphIdRegistry.Clear();
            Ludots.Core.NodeLibraries.GASGraph.Host.GraphIdRegistry.Register("Graph.Shield.Absorb");

            var registry = new EffectTemplateRegistry();
            var loader = new EffectTemplateLoader(pipeline, registry);

            Assert.DoesNotThrow(() => loader.Load("GAS/effects.json"),
                "Loading MobaDemoMod effects.json via VFS + ConfigPipeline must not throw");

            // Verify key MobaDemoMod templates are loaded
            int qDamageId = EffectTemplateIdRegistry.GetId("Effect.Moba.Damage.Q");
            int wHealId = EffectTemplateIdRegistry.GetId("Effect.Moba.Heal.W");
            int eDamageId = EffectTemplateIdRegistry.GetId("Effect.Moba.Damage.E");
            int rDamageId = EffectTemplateIdRegistry.GetId("Effect.Moba.Damage.R");

            That(qDamageId, Is.GreaterThan(0), "Effect.Moba.Damage.Q must be registered");
            That(wHealId, Is.GreaterThan(0), "Effect.Moba.Heal.W must be registered");
            That(eDamageId, Is.GreaterThan(0), "Effect.Moba.Damage.E must be registered");
            That(rDamageId, Is.GreaterThan(0), "Effect.Moba.Damage.R must be registered");

            That(registry.TryGet(qDamageId, out var qData), Is.True, "Q damage template should exist in registry");
            That(qData.LifetimeKind, Is.EqualTo(EffectLifetimeKind.Instant), "Q damage should be instant");
            That(qData.Modifiers.Count, Is.GreaterThan(0), "Q damage should have modifiers");
        }

        [Test]
        public void CoreEffectsJson_LoadsWithoutErrors()
        {
            string repoRoot = FindRepoRoot();
            string coreAssetsDir = Path.Combine(repoRoot, "assets");

            if (!Directory.Exists(coreAssetsDir))
            {
                Assert.Ignore("Core assets directory not found.");
                return;
            }

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", coreAssetsDir);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);

            EffectParamKeys.Initialize();
            EffectTemplateIdRegistry.Clear();

            var registry = new EffectTemplateRegistry();
            var loader = new EffectTemplateLoader(pipeline, registry);

            Assert.DoesNotThrow(() => loader.Load("GAS/effects.json"),
                "Core effects.json must load without exceptions");
        }

        // ════════════════════════════════════════════════════════════════════
        //  Helper
        // ════════════════════════════════════════════════════════════════════

        private static string FindRepoRoot()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null)
            {
                if (System.IO.Directory.Exists(System.IO.Path.Combine(dir, "assets")))
                    return dir;
                dir = System.IO.Directory.GetParent(dir)?.FullName;
            }
            throw new InvalidOperationException("Cannot find repo root (looking for assets/ directory).");
        }
    }
}
