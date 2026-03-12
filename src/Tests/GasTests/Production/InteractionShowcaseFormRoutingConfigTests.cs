using System;
using System.Collections.Generic;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [TestFixture]
    public sealed class InteractionShowcaseFormRoutingConfigTests
    {
        [Test]
        public void InteractionShowcase_TemplateConfig_LoadsAbilityGroupAndFormSetRouting()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");

            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod", "CoreInputMod", "CameraProfilesMod", "InteractionShowcaseMod" }),
                assetsRoot);

            var templates = new Dictionary<string, EntityTemplate>(StringComparer.OrdinalIgnoreCase);
            foreach (var template in engine.MapLoader.TemplateRegistry.GetAll())
            {
                templates[template.Id] = template;
            }

            var entity = new EntityBuilder(engine.World, templates)
                .UseTemplate("interaction_arcweaver_forms_demo")
                .Build();

            Assert.That(engine.World.Has<AbilityStateBuffer>(entity), Is.True);
            Assert.That(engine.World.Has<AbilityFormSetRef>(entity), Is.True);
            Assert.That(engine.World.Has<AbilityFormSlotBuffer>(entity), Is.True);
            Assert.That(engine.World.Has<PlayerOwner>(entity), Is.False, "Entity templates must not bake scene ownership.");

            ref var abilities = ref engine.World.Get<AbilityStateBuffer>(entity);
            Assert.That(abilities.Count, Is.EqualTo(4));

            int meleeTagId = TagRegistry.GetId("State.Form.Melee");
            Assert.That(meleeTagId, Is.GreaterThan(0), "Ability form config should register the form tags.");

            ref var tags = ref engine.World.Get<GameplayTagContainer>(entity);
            tags.AddTag(meleeTagId);

            var routing = new AbilityFormRoutingSystem(
                engine.World,
                engine.GetService(CoreServiceKeys.AbilityFormSetRegistry),
                engine.GetService(CoreServiceKeys.TagOps));
            routing.Update(0f);

            ref var formSlots = ref engine.World.Get<AbilityFormSlotBuffer>(entity);
            var grantedSlots = default(GrantedSlotBuffer);
            int meleeQ = AbilityIdRegistry.GetId("Ability.Interaction.Arcweaver.FireLance");
            int meleeW = AbilityIdRegistry.GetId("Ability.Interaction.Arcweaver.ArcDash");

            Assert.That(AbilitySlotResolver.Resolve(in abilities, in formSlots, hasForm: true, in grantedSlots, hasGranted: false, slotIndex: 0).AbilityId, Is.EqualTo(meleeQ));
            Assert.That(AbilitySlotResolver.Resolve(in abilities, in formSlots, hasForm: true, in grantedSlots, hasGranted: false, slotIndex: 1).AbilityId, Is.EqualTo(meleeW));
        }

        [Test]
        public void InteractionShowcase_HubMap_InjectsPlayerOwnershipAtInstanceLevel()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");

            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod", "CoreInputMod", "CameraProfilesMod", "InteractionShowcaseMod" }),
                assetsRoot);
            engine.Start();
            engine.LoadMap("interaction_showcase_hub");
            engine.Tick(1f / 60f);

            AssertNamedEntityOwner(engine.World, "Arcweaver", expectedPlayerId: 1);
            AssertNamedEntityOwner(engine.World, "Vanguard", expectedPlayerId: 1);
            AssertNamedEntityOwner(engine.World, "Commander", expectedPlayerId: 1);
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                var srcDir = Path.Combine(dir.FullName, "src");
                var assetsDir = Path.Combine(dir.FullName, "assets");
                if (Directory.Exists(srcDir) && Directory.Exists(assetsDir))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
        }

        private static void AssertNamedEntityOwner(World world, string entityName, int expectedPlayerId)
        {
            Entity found = default;
            var query = new QueryDescription().WithAll<Name, PlayerOwner>();
            world.Query(in query, (Entity entity, ref Name name, ref PlayerOwner owner) =>
            {
                if (found != default || !string.Equals(name.Value, entityName, StringComparison.Ordinal))
                {
                    return;
                }

                Assert.That(owner.PlayerId, Is.EqualTo(expectedPlayerId), $"{entityName} should receive PlayerOwner from map instance overrides.");
                found = entity;
            });

            Assert.That(world.IsAlive(found), Is.True, $"Expected map instance '{entityName}' with PlayerOwner.");
        }
    }
}
