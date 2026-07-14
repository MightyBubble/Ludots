using System;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class GasConfigLoaderFailFastTests
    {
        private string _root = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "Ludots_GasConfigLoaderFailFastTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            AbilityIdRegistry.Clear();
            ContextGroupIdRegistry.Clear();
            AbilityFormSetIdRegistry.Clear();
            AttributeRegistry.Clear();
            TagRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            }
            catch
            {
            }
        }

        [Test]
        public void AttributeConstraintsLoader_InvalidBool_IsRejected()
        {
            WriteConfig("config_catalog.json",
                @"[{ ""Path"": ""GAS/attribute_constraints.json"", ""Policy"": ""DeepObject"" }]");
            WriteConfig("GAS/attribute_constraints.json",
                @"{ ""Health"": { ""clampToBase"": ""sometimes"", ""min"": 0 } }");

            var (pipeline, catalog) = BuildPipeline();
            var loader = new AttributeConstraintsLoader(pipeline);

            Throws<InvalidOperationException>(() => loader.Load(catalog));
        }

        [Test]
        public void AttributeConstraintsLoader_NonObjectEntry_IsRejected()
        {
            WriteConfig("config_catalog.json",
                @"[{ ""Path"": ""GAS/attribute_constraints.json"", ""Policy"": ""DeepObject"" }]");
            WriteConfig("GAS/attribute_constraints.json",
                @"{ ""Health"": 12 }");

            var (pipeline, catalog) = BuildPipeline();
            var loader = new AttributeConstraintsLoader(pipeline);

            var ex = Throws<InvalidOperationException>(() => loader.Load(catalog));

            That(ex!.Message, Does.Contain("Health"));
            That(ex.Message, Does.Contain("object"));
        }

        [Test]
        public void ContextGroupConfigLoader_NonObjectCandidate_IsRejected()
        {
            WriteConfig("config_catalog.json",
                @"[{ ""Path"": ""GAS/context_groups.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteConfig("GAS/context_groups.json",
                """
                [
                  {
                    "id": "strict_context",
                    "rootAbilityId": "Ability.Root",
                    "searchRadiusCm": 500,
                    "candidates": [12]
                  }
                ]
                """);

            AbilityIdRegistry.Register("Ability.Root");
            var (pipeline, catalog) = BuildPipeline();
            var loader = new ContextGroupConfigLoader(pipeline, new ContextGroupRegistry());

            var ex = Throws<InvalidOperationException>(() => loader.Load(catalog));

            That(ex!.Message, Does.Contain("candidates[0]"));
            That(ex.Message, Does.Contain("object"));
        }

        [Test]
        public void ContextGroupConfigLoader_TargetedCandidateRequiresScoringFields()
        {
            WriteConfig("config_catalog.json",
                @"[{ ""Path"": ""GAS/context_groups.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteConfig("GAS/context_groups.json",
                """
                [
                  {
                    "id": "strict_context",
                    "rootAbilityId": "Ability.Root",
                    "searchRadiusCm": 500,
                    "candidates": [
                      {
                        "abilityId": "Ability.Candidate",
                        "basePriority": 10,
                        "requiresTarget": true
                      }
                    ]
                  }
                ]
                """);

            AbilityIdRegistry.Register("Ability.Root");
            AbilityIdRegistry.Register("Ability.Candidate");
            var (pipeline, catalog) = BuildPipeline();
            var loader = new ContextGroupConfigLoader(pipeline, new ContextGroupRegistry());

            var ex = Throws<InvalidOperationException>(() => loader.Load(catalog));

            That(ex!.Message, Does.Contain("maxDistanceCm"));
        }

        [Test]
        public void ContextGroupConfigLoader_RequiresTargetIsRequired()
        {
            WriteConfig("config_catalog.json",
                @"[{ ""Path"": ""GAS/context_groups.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteConfig("GAS/context_groups.json",
                """
                [
                  {
                    "id": "strict_context",
                    "rootAbilityId": "Ability.Root",
                    "searchRadiusCm": 500,
                    "candidates": [
                      {
                        "abilityId": "Ability.Candidate",
                        "basePriority": 10
                      }
                    ]
                  }
                ]
                """);

            AbilityIdRegistry.Register("Ability.Root");
            AbilityIdRegistry.Register("Ability.Candidate");
            var (pipeline, catalog) = BuildPipeline();
            var loader = new ContextGroupConfigLoader(pipeline, new ContextGroupRegistry());

            var ex = Throws<InvalidOperationException>(() => loader.Load(catalog));

            That(ex!.Message, Does.Contain("candidates[0].requiresTarget"));
        }

        [Test]
        public void AbilityFormSetConfigLoader_RoutePriorityIsRequired()
        {
            WriteConfig("config_catalog.json",
                @"[{ ""Path"": ""GAS/ability_form_sets.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteConfig("GAS/ability_form_sets.json",
                """
                [
                  {
                    "id": "strict_forms",
                    "routes": [
                      {
                        "slotOverrides": [
                          { "slotIndex": 0, "abilityId": "Ability.Slot" }
                        ]
                      }
                    ]
                  }
                ]
                """);

            AbilityIdRegistry.Register("Ability.Slot");
            var (pipeline, catalog) = BuildPipeline();
            var loader = new AbilityFormSetConfigLoader(pipeline, new AbilityFormSetRegistry());

            var ex = Throws<InvalidOperationException>(() => loader.Load(catalog));

            That(ex!.Message, Does.Contain("priority"));
        }

        [Test]
        public void AbilityFormSetConfigLoader_EmptyTagString_IsRejected()
        {
            WriteConfig("config_catalog.json",
                @"[{ ""Path"": ""GAS/ability_form_sets.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteConfig("GAS/ability_form_sets.json",
                """
                [
                  {
                    "id": "strict_forms",
                    "routes": [
                      {
                        "requiredAll": [""],
                        "priority": 10,
                        "slotOverrides": [
                          { "slotIndex": 0, "abilityId": "Ability.Slot" }
                        ]
                      }
                    ]
                  }
                ]
                """);

            AbilityIdRegistry.Register("Ability.Slot");
            var (pipeline, catalog) = BuildPipeline();
            var loader = new AbilityFormSetConfigLoader(pipeline, new AbilityFormSetRegistry());

            var ex = Throws<InvalidOperationException>(() => loader.Load(catalog));

            That(ex!.Message, Does.Contain("requiredAll[0]"));
        }

        private (ConfigPipeline Pipeline, ConfigCatalog Catalog) BuildPipeline()
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(_root, "Core"));
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);
            return (pipeline, ConfigCatalogLoader.Load(pipeline));
        }

        private void WriteConfig(string relativePath, string json)
        {
            string fullPath = Path.Combine(_root, "Core", "Configs", relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, json);
        }
    }
}
