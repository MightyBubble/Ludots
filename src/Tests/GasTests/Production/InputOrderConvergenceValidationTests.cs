using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [TestFixture]
    public sealed class InputOrderConvergenceValidationTests
    {
        [Test]
        public void CoreBootstrap_AttributeCalculation_RegistersAggregatorBeforeBinding()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");

            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod" }),
                assetsRoot);

            var attributeSystems = GetSystems(engine, SystemGroup.AttributeCalculation);

            Assert.That(attributeSystems.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(attributeSystems[0].GetType().Name, Is.EqualTo("AttributeAggregatorSystem"));
            Assert.That(attributeSystems[1].GetType().Name, Is.EqualTo("AttributeBindingSystem"));
        }

        [Test]
        public void MobaBootstrap_GameStart_RegistersGameplayInputSystemsInFixedStep()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");

            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod", "CoreInputMod", "MobaDemoMod" }),
                assetsRoot);
            engine.Start();

            var inputNames = GetSystemNames(engine, SystemGroup.InputCollection);
            var presentationNames = GetPresentationSystemNames(engine);

            Assert.That(inputNames, Does.Contain("AuthoritativeInputSnapshotSystem"));
            Assert.That(inputNames, Does.Contain("LocalPlayerEntityResolverSystem"));
            Assert.That(inputNames, Does.Contain("AbilityFormRoutingSystem"));
            Assert.That(inputNames, Does.Contain("CurrentSelectionApplySystem"));
            Assert.That(inputNames, Does.Contain("GasSelectionResponseSystem"));
            Assert.That(inputNames, Does.Contain("GasInputResponseSystem"));
            Assert.That(inputNames, Does.Contain("TabTargetCycleSystem"));
            Assert.That(inputNames, Does.Contain("ViewModeSwitchSystem"));
            Assert.That(inputNames, Does.Contain("MobaLocalOrderSourceSystem"));
            Assert.That(inputNames.IndexOf("AuthoritativeInputSnapshotSystem"), Is.LessThan(inputNames.IndexOf("CurrentSelectionApplySystem")));
            Assert.That(inputNames.IndexOf("AuthoritativeInputSnapshotSystem"), Is.LessThan(inputNames.IndexOf("GasSelectionResponseSystem")));
            Assert.That(inputNames.IndexOf("AbilityFormRoutingSystem"), Is.LessThan(inputNames.IndexOf("MobaLocalOrderSourceSystem")));
            Assert.That(inputNames.IndexOf("AuthoritativeInputSnapshotSystem"), Is.LessThan(inputNames.IndexOf("MobaLocalOrderSourceSystem")));

            Assert.That(presentationNames, Does.Not.Contain("LocalPlayerEntityResolverSystem"));
            Assert.That(presentationNames, Does.Not.Contain("CurrentSelectionApplySystem"));
            Assert.That(presentationNames, Does.Not.Contain("GasSelectionResponseSystem"));
            Assert.That(presentationNames, Does.Not.Contain("GasInputResponseSystem"));
            Assert.That(presentationNames, Does.Not.Contain("TabTargetCycleSystem"));
            Assert.That(presentationNames, Does.Not.Contain("ViewModeSwitchSystem"));
            Assert.That(presentationNames, Does.Not.Contain("MobaLocalOrderSourceSystem"));
            Assert.That(presentationNames, Does.Contain("AbilityAimOverlayPresentationSystem"));
            Assert.That(presentationNames, Does.Contain("SelectedMovePathPresentationSystem"));
            Assert.That(presentationNames, Does.Contain("SkillBarOverlaySystem"));
            Assert.That(presentationNames, Does.Contain("WorldToVisualSyncSystem"));
            Assert.That(presentationNames, Does.Contain("TerrainHeightSyncSystem"));
        }

        [Test]
        public void CoreBootstrap_OrderTypes_LoadExtendedConfigFromJson()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");

            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod" }),
                assetsRoot);

            var config = engine.GetService(CoreServiceKeys.GameConfig);
            var orderTypes = engine.GetService(CoreServiceKeys.OrderTypeRegistry);

            var cast = orderTypes.Get(config.Constants.OrderTypeIds["castAbility"]);
            Assert.That(cast.PendingBufferWindowMs, Is.EqualTo(400));
            Assert.That(cast.SpatialBlackboardKey, Is.EqualTo(OrderBlackboardKeys.Cast_TargetPosition));
            Assert.That(cast.EntityBlackboardKey, Is.EqualTo(OrderBlackboardKeys.Cast_TargetEntity));
            Assert.That(cast.IntArg0BlackboardKey, Is.EqualTo(OrderBlackboardKeys.Cast_SlotIndex));
            Assert.That(cast.ValidationGraphId, Is.EqualTo(0));

            var attack = orderTypes.Get(config.Constants.OrderTypeIds["attackTarget"]);
            Assert.That(attack.PendingBufferWindowMs, Is.EqualTo(400));
            Assert.That(attack.SpatialBlackboardKey, Is.EqualTo(OrderBlackboardKeys.Attack_MovePosition));
            Assert.That(attack.EntityBlackboardKey, Is.EqualTo(OrderBlackboardKeys.Attack_TargetEntity));
            Assert.That(attack.IntArg0BlackboardKey, Is.EqualTo(-1));
            Assert.That(attack.ValidationGraphId, Is.EqualTo(0));

            var stop = orderTypes.Get(config.Constants.OrderTypeIds["stop"]);
            Assert.That(stop.PendingBufferWindowMs, Is.EqualTo(400));
            Assert.That(stop.SpatialBlackboardKey, Is.EqualTo(-1));
            Assert.That(stop.EntityBlackboardKey, Is.EqualTo(-1));
            Assert.That(stop.IntArg0BlackboardKey, Is.EqualTo(-1));
            Assert.That(stop.ValidationGraphId, Is.EqualTo(0));

            var chainPass = orderTypes.Get(config.Constants.ResponseChainOrderTypeIds["chainPass"]);
            Assert.That(chainPass.Label, Is.EqualTo("Pass"));
            Assert.That(chainPass.PendingBufferWindowMs, Is.EqualTo(0));
            Assert.That(chainPass.AllowQueuedMode, Is.False);
            Assert.That(chainPass.QueueFullPolicy, Is.EqualTo(QueueFullPolicy.RejectNew));

            var chainNegate = orderTypes.Get(config.Constants.ResponseChainOrderTypeIds["chainNegate"]);
            Assert.That(chainNegate.Label, Is.EqualTo("Negate"));
            Assert.That(chainNegate.PendingBufferWindowMs, Is.EqualTo(0));
            Assert.That(chainNegate.AllowQueuedMode, Is.False);
            Assert.That(chainNegate.QueueFullPolicy, Is.EqualTo(QueueFullPolicy.RejectNew));

            var chainActivateEffect = orderTypes.Get(config.Constants.ResponseChainOrderTypeIds["chainActivateEffect"]);
            Assert.That(chainActivateEffect.Label, Is.EqualTo("Chain"));
            Assert.That(chainActivateEffect.PendingBufferWindowMs, Is.EqualTo(0));
            Assert.That(chainActivateEffect.AllowQueuedMode, Is.False);
            Assert.That(chainActivateEffect.QueueFullPolicy, Is.EqualTo(QueueFullPolicy.RejectNew));
        }

        [Test]
        public void GameEngine_OrderTypeBootstrap_HasNoImplicitOrderTypeFallbacks()
        {
            string repoRoot = FindRepoRoot();
            string source = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "Engine", "GameEngine.cs"));

            Assert.That(source, Does.Not.Contain("GetValueOrDefault(\"chainPass\""));
            Assert.That(source, Does.Not.Contain("GetValueOrDefault(\"chainNegate\""));
            Assert.That(source, Does.Not.Contain("GetValueOrDefault(\"chainActivateEffect\""));
            Assert.That(
                Regex.IsMatch(source, @"TryGetId\(\s*""castAbility\.Start""[\s\S]*\?\s*[\w]+\s*:\s*0"),
                Is.False);
            Assert.That(
                Regex.IsMatch(source, @"TryGetId\(\s*""castAbility\.End""[\s\S]*\?\s*[\w]+\s*:\s*0"),
                Is.False);

            Assert.That(source, Does.Contain("RequireConfiguredOrderTypeId(responseChainOrderTypeIds, orderTypeRegistry, \"chainPass\""));
            Assert.That(source, Does.Contain("RequireRegisteredOrderTypeId(orderTypeRegistry, \"castAbility.Start\")"));
            Assert.That(source, Does.Contain("RequireRegisteredOrderTypeId(orderTypeRegistry, \"castAbility.End\")"));
        }

        [Test]
        public void InputAndPanelSources_HaveNoImplicitOrderTypeFallbacks()
        {
            string repoRoot = FindRepoRoot();
            string inputMappingLoader = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "Input", "Orders", "InputOrderMappingLoader.cs"));
            string inputMappingSystem = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "Input", "Orders", "InputOrderMappingSystem.cs"));
            string localOrderSource = File.ReadAllText(Path.Combine(repoRoot, "mods", "CoreInputMod", "Systems", "LocalOrderSourceHelper.cs"));
            string responseChainSource = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "Presentation", "Systems", "ResponseChainHumanOrderSourceSystem.cs"));
            string commandPanelSource = File.ReadAllText(Path.Combine(repoRoot, "mods", "EntityCommandPanelMod", "Runtime", "GasEntityCommandPanelSource.cs"));

            Assert.That(inputMappingLoader, Does.Not.Contain("CreateDefaultMobaConfig"));
            Assert.That(inputMappingSystem, Does.Not.Contain("orderTypeId <= 0) return false"));
            Assert.That(localOrderSource, Does.Not.Contain("? configOrderTypeId : 0"));
            Assert.That(responseChainSource, Does.Not.Contain("GetValueOrDefault(\"chainPass\""));
            Assert.That(responseChainSource, Does.Not.Contain("ResponseChainOrderTypes.Default"));
            Assert.That(commandPanelSource, Does.Not.Contain("OrderTypeId == 100"));
        }

        [Test]
        public void Cfg4Guardrails_FailFastAntipatternsDoNotReturn()
        {
            string repoRoot = FindRepoRoot();
            string aiConfigLoader = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "Gameplay", "AI", "Config", "AiConfigLoader.cs"));
            string abilityExecLoader = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "Gameplay", "GAS", "Config", "AbilityExecLoader.cs"));
            string configMerger = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "Config", "ConfigMerger.cs"));
            string inputMappingLoader = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "Input", "Orders", "InputOrderMappingLoader.cs"));
            string localOrderSource = File.ReadAllText(Path.Combine(repoRoot, "mods", "CoreInputMod", "Systems", "LocalOrderSourceHelper.cs"));
            string responseChainSource = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "Presentation", "Systems", "ResponseChainHumanOrderSourceSystem.cs"));
            string commandPanelSource = File.ReadAllText(Path.Combine(repoRoot, "mods", "EntityCommandPanelMod", "Runtime", "GasEntityCommandPanelSource.cs"));

            Assert.That(aiConfigLoader, Does.Contain("OrderTagId is not a supported AI order contract field"));
            Assert.That(aiConfigLoader, Does.Contain("Order blackboard key must be a semantic string"));
            Assert.That(aiConfigLoader, Does.Not.Contain("? ot : 0"));
            Assert.That(aiConfigLoader, Does.Not.Contain("TryGetValue(\"OrderTypeId\""));

            Assert.That(inputMappingLoader, Does.Not.Contain("CreateDefaultMobaConfig"));
            Assert.That(localOrderSource, Does.Not.Contain("? v : 0"));
            Assert.That(responseChainSource, Does.Not.Contain("GetValueOrDefault(\"chainPass\""));
            Assert.That(responseChainSource, Does.Not.Contain("ResponseChainOrderTypes.Default"));
            Assert.That(commandPanelSource, Does.Not.Contain("OrderTypeId == 100"));

            Assert.That(abilityExecLoader, Does.Contain("field 'exec' is required"));
            Assert.That(abilityExecLoader, Does.Not.Contain("if (idx >= AbilityExecSpec.MAX_ITEMS) break"));
            Assert.That(abilityExecLoader, Does.Not.Contain("if (tid <= 0) continue"));

            Assert.That(configMerger, Does.Contain("must define non-empty string field"));
            Assert.That(configMerger, Does.Not.Contain("if (!TryReadId(obj, entry.IdField, out string id)) continue"));
        }

        [Test]
        public void Cfg4Guardrails_ProductionAiConfigsUseSemanticKeys()
        {
            string repoRoot = FindRepoRoot();
            var aiConfigFiles = Directory.GetFiles(Path.Combine(repoRoot, "mods"), "*.json", SearchOption.AllDirectories);
            foreach (string file in aiConfigFiles)
            {
                string normalized = file.Replace('\\', '/');
                if (!normalized.Contains("/assets/Configs/AI/", StringComparison.Ordinal))
                {
                    continue;
                }

                string source = File.ReadAllText(file);
                Assert.That(source, Does.Not.Contain("\"OrderTagId\""), file);
                Assert.That(Regex.IsMatch(source, @"""(?:EntityKey|IntKey)""\s*:\s*\d+"), Is.False, file);
            }
        }

        private static List<ISystem<float>> GetSystems(GameEngine engine, SystemGroup group)
        {
            var field = typeof(GameEngine).GetField("_systemGroups", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);

            var systemGroups = field!.GetValue(engine) as Dictionary<SystemGroup, List<ISystem<float>>>;
            Assert.That(systemGroups, Is.Not.Null);
            Assert.That(systemGroups!.ContainsKey(group), Is.True);

            return systemGroups[group];
        }

        private static List<string> GetSystemNames(GameEngine engine, SystemGroup group)
        {
            var systems = GetSystems(engine, group);
            var names = new List<string>(systems.Count);
            for (int i = 0; i < systems.Count; i++)
            {
                names.Add(systems[i].GetType().Name);
            }

            return names;
        }

        private static List<string> GetPresentationSystemNames(GameEngine engine)
        {
            var field = typeof(GameEngine).GetField("_presentationSystems", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);

            var systems = field!.GetValue(engine) as List<ISystem<float>>;
            Assert.That(systems, Is.Not.Null);

            var names = new List<string>(systems!.Count);
            for (int i = 0; i < systems.Count; i++)
            {
                names.Add(systems[i].GetType().Name);
            }

            return names;
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
    }
}
