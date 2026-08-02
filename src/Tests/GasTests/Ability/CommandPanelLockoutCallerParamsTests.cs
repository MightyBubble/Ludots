using Arch.Core;
using EntityCommandPanelMod;
using System;
using System.IO;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [NonParallelizable]
    public sealed class CommandPanelLockoutCallerParamsTests
    {
        [SetUp]
        public void SetUp()
        {
            EffectParamKeys.Initialize();
            TagRegistry.Clear();
            EffectTemplateIdRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            TagRegistry.Clear();
            EffectTemplateIdRegistry.Clear();
        }

        [Test]
        public void CopySlots_LockoutPermille_ProjectsActiveEffectGrantedByAbilityExecution()
        {
            using var engine = new GameEngine();
            string repoRoot = FindRepoRoot();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod" }),
                Path.Combine(repoRoot, "assets"));
            World world = engine.World ?? throw new InvalidOperationException("GameEngine world was not initialized.");

            var clock = new DiscreteClock();
            var abilityDefinitions = new AbilityDefinitionRegistry();
            var effectTemplates = new EffectTemplateRegistry();
            var effectRequests = new EffectRequestQueue();
            engine.SetService(CoreServiceKeys.Clock, clock);
            engine.SetService(CoreServiceKeys.AbilityDefinitionRegistry, abilityDefinitions);
            engine.SetService(CoreServiceKeys.EffectTemplateRegistry, effectTemplates);

            int lockoutTagId = TagRegistry.Register("Cooldown.Tests.DurationOverride");
            int lockoutEffectTemplateId = EffectTemplateIdRegistry.Register("Effect.Tests.Lockout");
            var grantedTags = new EffectGrantedTags();
            Assert.That(grantedTags.Add(new TagContribution
            {
                TagId = lockoutTagId,
                Formula = TagContributionFormula.Fixed,
                Amount = 1
            }), Is.True);
            effectTemplates.Register(lockoutEffectTemplateId, new EffectTemplateData
            {
                PresetType = EffectPresetType.None,
                LifetimeKind = EffectLifetimeKind.After,
                ClockId = GasClockId.Step,
                DurationTicks = 30,
                GrantedTags = grantedTags,
            });
            FinalizeEffectTemplates(effectTemplates, "CommandPanelLockoutCallerParamsTests.LockoutEffect");

            const int abilityId = 1001;
            var execSpec = default(AbilityExecSpec);
            execSpec.SetItem(
                0,
                ExecItemKind.EffectSignal,
                tick: 0,
                templateId: lockoutEffectTemplateId,
                callerParamsIdx: 0,
                payloadA: (int)ExecEffectDispatchTarget.Source);

            var callerParams = default(EffectConfigParams);
            Assert.That(callerParams.TryAddInt(EffectParamKeys.DurationTicks, 60), Is.True);
            var callerPool = default(AbilityExecCallerParamsPool);
            Assert.That(callerPool.TryAdd(in callerParams), Is.True);

            var blockTags = default(AbilityActivationBlockTags);
            blockTags.BlockedAny.AddTag(lockoutTagId);
            var ability = new AbilityDefinition
            {
                ExecSpec = execSpec,
                HasExecCallerParamsPool = true,
                ExecCallerParamsPool = callerPool,
                HasActivationBlockTags = true,
                ActivationBlockTags = blockTags,
                HasPresentation = true,
                Presentation = new AbilityPresentationConfig
                {
                    DisplayName = "Lockout Test",
                    HintText = "Uses caller params duration"
                }
            };
            abilityDefinitions.Register(abilityId, in ability, "CommandPanelLockoutCallerParamsTests");

            var abilityState = default(AbilityStateBuffer);
            abilityState.AddAbility(abilityId);
            Entity actor = world.Create(
                abilityState,
                new GameplayTagContainer(),
                new TagCountContainer(),
                new DirtyFlags(),
                new AbilityExecInstance
                {
                    AbilitySlot = 0,
                    AbilityId = abilityId,
                    State = AbilityExecRunState.Running,
                    ActiveClockId = GasClockId.Step,
                });

            var tagOps = new TagOps(new DirtyEntityQueue(capacity: 16), new TagRuleRegistry());
            var abilityExecSystem = new AbilityExecSystem(
                world,
                clock,
                new InputRequestQueue(),
                new InputResponseBuffer(),
                effectRequests,
                snapshotCapacity: 16,
                abilityDefinitions: abilityDefinitions,
                tagOps: tagOps);
            var proposalSystem = new EffectProposalProcessingSystem(
                world,
                effectRequests,
                GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                clock,
                budget: new GasBudget(),
                templates: effectTemplates,
                responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                tagOps: tagOps);
            var applicationSystem = new EffectApplicationSystem(
                world,
                GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                clock,
                effectRequests,
                templates: effectTemplates,
                tagOps: tagOps);
            var lifetimeSystem = new EffectLifetimeSystem(
                world,
                clock,
                new GasConditionRegistry(),
                snapshotCapacity: 16,
                fanOutCommandCapacity: GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                effectRequests: effectRequests,
                templates: effectTemplates,
                tagOps: tagOps);

            abilityExecSystem.Update(0f);
            Assert.That(effectRequests.Count, Is.EqualTo(1));
            proposalSystem.Update(0f);
            applicationSystem.Update(0f);
            Assert.That(world.Has<ActiveEffectContainer>(actor), Is.True);
            Assert.That(world.Get<GameplayTagContainer>(actor).HasTag(lockoutTagId), Is.True);

            lifetimeSystem.Update(0f);
            clock.Advance(ClockDomainId.Step, 30);
            lifetimeSystem.Update(0f);

            var source = CreateGasAbilitySource(engine);
            var slots = new EntityCommandPanelSlotView[1];
            var context = new EntityCommandPanelSourceContext(actor, "gas.ability-slots", string.Empty);
            int copied = EntityCommandPanelSourceDispatch.CopySlots(source, in context, groupIndex: 0, slots);

            Assert.That(copied, Is.EqualTo(1));
            Assert.That(slots[0].AbilityId, Is.EqualTo(abilityId));
            Assert.That(slots[0].LockoutPermille, Is.EqualTo(500));
        }

        [Test]
        public void CopySlots_LockoutPermille_IgnoresTimedTagWithoutActiveGrantedEffect()
        {
            using var engine = new GameEngine();
            string repoRoot = FindRepoRoot();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod" }),
                Path.Combine(repoRoot, "assets"));
            World world = engine.World ?? throw new InvalidOperationException("GameEngine world was not initialized.");

            var clock = new DiscreteClock();
            var abilityDefinitions = new AbilityDefinitionRegistry();
            engine.SetService(CoreServiceKeys.Clock, clock);
            engine.SetService(CoreServiceKeys.AbilityDefinitionRegistry, abilityDefinitions);

            int lockoutTagId = TagRegistry.Register("Cooldown.Tests.TimedTagShadow");
            const int abilityId = 1002;
            var blockTags = default(AbilityActivationBlockTags);
            blockTags.BlockedAny.AddTag(lockoutTagId);
            var ability = new AbilityDefinition
            {
                HasActivationBlockTags = true,
                ActivationBlockTags = blockTags,
                HasPresentation = true,
                Presentation = new AbilityPresentationConfig
                {
                    DisplayName = "Timed Shadow",
                    HintText = "Timed tags alone are not UI lockout facts"
                }
            };
            abilityDefinitions.Register(abilityId, in ability, "CommandPanelLockoutCallerParamsTests");

            var abilityState = default(AbilityStateBuffer);
            abilityState.AddAbility(abilityId);
            Entity actor = world.Create(abilityState, new TimedTagBuffer());
            ref var timedTags = ref world.Get<TimedTagBuffer>(actor);
            Assert.That(timedTags.TryAdd(lockoutTagId, expireAt: 30, GasClockId.Step), Is.True);

            var source = CreateGasAbilitySource(engine);
            var slots = new EntityCommandPanelSlotView[1];
            var context = new EntityCommandPanelSourceContext(actor, "gas.ability-slots", string.Empty);
            int copied = EntityCommandPanelSourceDispatch.CopySlots(source, in context, groupIndex: 0, slots);

            Assert.That(copied, Is.EqualTo(1));
            Assert.That(slots[0].AbilityId, Is.EqualTo(abilityId));
            Assert.That(slots[0].LockoutPermille, Is.EqualTo(0));
        }

        private static IEntityCommandPanelSource CreateGasAbilitySource(GameEngine engine)
        {
            Type sourceType = typeof(EntityCommandPanelModEntry).Assembly.GetType(
                "EntityCommandPanelMod.Runtime.GasEntityCommandPanelSource",
                throwOnError: true)!;
            return (IEntityCommandPanelSource)Activator.CreateInstance(sourceType, engine)!;
        }

        private static string FindRepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "README.md")) && Directory.Exists(Path.Combine(dir, "mods")))
                {
                    return dir;
                }

                dir = Directory.GetParent(dir)?.FullName ?? string.Empty;
            }

            throw new DirectoryNotFoundException("Could not locate repository root.");
        }

        private static void FinalizeEffectTemplates(EffectTemplateRegistry templates, string sourceName)
        {
            var presetTypes = new PresetTypeRegistry();
            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);
            EffectExecutionPlanCompiler.FinalizeAll(
                templates,
                presetTypes,
                builtinHandlers,
                new GraphProgramRegistry(),
                GasGraphOpHandlerTable.Instance,
                sourceName);
        }
    }
}
