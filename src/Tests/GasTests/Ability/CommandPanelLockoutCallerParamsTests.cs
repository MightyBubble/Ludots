using Arch.Core;
using EntityCommandPanelMod;
using System;
using System.IO;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
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
        public void CopySlots_LockoutPermille_UsesEffectCallerParamsDurationOverride()
        {
            using var engine = new GameEngine();
            string repoRoot = FindRepoRoot();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod" }),
                Path.Combine(repoRoot, "assets"));

            var clock = new DiscreteClock();
            var abilityDefinitions = new AbilityDefinitionRegistry();
            var effectTemplates = new EffectTemplateRegistry();
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
                LifetimeKind = EffectLifetimeKind.After,
                ClockId = GasClockId.Step,
                DurationTicks = 30,
                GrantedTags = grantedTags,
            });

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

            Entity actor = engine.World.Create(new AbilityStateBuffer(), new TimedTagBuffer());
            ref var abilities = ref engine.World.Get<AbilityStateBuffer>(actor);
            abilities.AddAbility(abilityId);
            ref var timedTags = ref engine.World.Get<TimedTagBuffer>(actor);
            Assert.That(timedTags.TryAdd(lockoutTagId, expireAt: 30, GasClockId.Step), Is.True);

            var source = CreateGasAbilitySource(engine);
            var slots = new EntityCommandPanelSlotView[1];
            var context = new EntityCommandPanelSourceContext(actor, "gas.ability-slots", string.Empty);
            int copied = EntityCommandPanelSourceDispatch.CopySlots(source, in context, groupIndex: 0, slots);

            Assert.That(copied, Is.EqualTo(1));
            Assert.That(slots[0].AbilityId, Is.EqualTo(abilityId));
            Assert.That(slots[0].LockoutPermille, Is.EqualTo(500));
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
    }
}
