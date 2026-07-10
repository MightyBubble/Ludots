using System;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class ScenarioPlanConfigLoaderTests
    {
        [Test]
        public void Load_Valid1v1Plan_ParsesPlacementsOwnershipAndRelationships()
        {
            string root = CreateTempRoot("scenario-plan-valid");
            try
            {
                WriteCatalog(root);
                WritePlans(root,
                    """
                    [
                      {
                        "id": "scenario.arena.1v1",
                        "mapId": "maps/arena",
                        "seed": 42,
                        "layout": { "spawnSpreadCm": 500 },
                        "placements": [
                          {
                            "id": "spawn.alpha",
                            "templateId": "unit.warrior",
                            "position": { "x": 1000, "y": 2000 },
                            "facingAngleRad": 1.57,
                            "teamId": 1,
                            "playerOwnerId": 1,
                            "componentPatches": [
                              { "componentName": "Health", "data": { "Current": 100, "Max": 100 } }
                            ],
                            "performerParamOverrides": [
                              { "paramKey": "tint.r", "lane": "Float", "floatValue": 0.2 },
                              { "paramKey": "unit.level", "lane": "Int", "intValue": 3 },
                              { "paramKey": "marker.color", "lane": "Vector", "vectorValue": [1, 0, 0, 1] }
                            ]
                          },
                          {
                            "id": "spawn.bravo",
                            "templateId": "unit.warrior",
                            "position": { "x": 9000, "y": 2000 },
                            "teamId": 2,
                            "playerOwnerId": 2
                          }
                        ],
                        "teams": [
                          { "teamId": 1, "representativePlacementId": "spawn.alpha" },
                          { "teamId": 2, "representativePlacementId": "spawn.bravo" }
                        ],
                        "players": [
                          { "playerId": 1, "teamId": 1, "representativePlacementId": "spawn.alpha" },
                          { "playerId": 2, "teamId": 2, "representativePlacementId": "spawn.bravo" }
                        ],
                        "initialRelationships": {
                          "teams": [
                            { "teamA": 1, "teamB": 2, "typeId": "Relationship.Hostile", "attitude": "Hostile", "symmetric": true }
                          ]
                        }
                      }
                    ]
                    """);

                DataRegistry<ScenarioPlan> registry = Load(root);
                ScenarioPlan plan = registry.Get("scenario.arena.1v1");

                Assert.That(plan, Is.Not.Null);
                Assert.That(plan!.MapId, Is.EqualTo("maps/arena"));
                Assert.That(plan.Seed, Is.EqualTo(42));
                Assert.That(plan.Layout, Is.Not.Null);
                Assert.That(plan.Layout!["spawnSpreadCm"]!.GetValue<int>(), Is.EqualTo(500));
                Assert.That(plan.Placements, Has.Count.EqualTo(2));
                Assert.That(plan.Placements[0].TemplateId, Is.EqualTo("unit.warrior"));
                Assert.That(plan.Placements[0].Position!.Value.X, Is.EqualTo(1000));
                Assert.That(plan.Placements[0].Position!.Value.Y, Is.EqualTo(2000));
                Assert.That(plan.Placements[0].FacingAngleRad, Is.EqualTo(1.57f).Within(0.0001f));
                Assert.That(plan.Placements[0].ComponentPatches, Has.Count.EqualTo(1));
                Assert.That(plan.Placements[0].ComponentPatches[0].ComponentName, Is.EqualTo("Health"));
                Assert.That(plan.Placements[0].PerformerParamOverrides, Has.Count.EqualTo(3));
                Assert.That(plan.Placements[0].PerformerParamOverrides[0].ParamKey, Is.EqualTo("tint.r"));
                Assert.That(plan.Placements[0].PerformerParamOverrides[1].IntValue, Is.EqualTo(3));
                Assert.That(plan.Placements[0].PerformerParamOverrides[2].VectorValue, Is.EqualTo(new[] { 1f, 0f, 0f, 1f }));
                Assert.That(plan.Teams, Has.Count.EqualTo(2));
                Assert.That(plan.Players, Has.Count.EqualTo(2));
                Assert.That(plan.InitialRelationships, Is.Not.Null);
                Assert.That(plan.InitialRelationships!.Teams, Has.Count.EqualTo(1));
                Assert.That(plan.InitialRelationships.Teams[0].TypeId, Is.EqualTo("Relationship.Hostile"));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_RequiresCatalogTrackedPath()
        {
            string root = CreateTempRoot("scenario-plan-catalog");
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "Scenarios"));
                File.WriteAllText(Path.Combine(root, "Configs", "config_catalog.json"), "[]");
                File.WriteAllText(
                    Path.Combine(root, "Configs", "Scenarios", "scenario_plans.json"),
                    """[{ "id": "scenario.orphan", "mapId": "maps/arena", "placements": [] }]""");

                ConfigPipeline pipeline = CreatePipeline(root);
                ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);

                InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(
                    () => new ScenarioPlanConfigLoader(pipeline).Load(catalog));

                Assert.That(ex!.Message, Does.Contain("Scenarios/scenario_plans.json"));
                Assert.That(ex.Message, Does.Contain("Config catalog must explicitly declare"));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void RepoConfigCatalog_TracksScenarioPlansPath()
        {
            string catalogPath = Path.Combine(FindRepoRoot(), "assets", "Configs", "config_catalog.json");
            Assert.That(File.Exists(catalogPath), Is.True, $"Missing catalog at {catalogPath}");

            string json = File.ReadAllText(catalogPath);
            Assert.That(json, Does.Contain("\"Path\": \"Scenarios/scenario_plans.json\""));
            Assert.That(json, Does.Contain("\"Policy\": \"ArrayById\""));
        }

        [Test]
        public void Load_RejectsUnknownTopLevelField()
        {
            string root = CreateTempRoot("scenario-plan-unknown");
            try
            {
                WriteCatalog(root);
                WritePlans(root,
                    """
                    [
                      {
                        "id": "scenario.unknown",
                        "mapId": "maps/arena",
                        "mysteryBag": true,
                        "placements": []
                      }
                    ]
                    """);

                InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => Load(root));
                Assert.That(ex!.Message, Does.Contain("unknown top-level field 'mysteryBag'"));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_RejectsForbiddenMapAndRulesetFields()
        {
            AssertForbiddenRejected("terrain", """{ "id": "s", "mapId": "m", "terrain": {}, "placements": [] }""");
            AssertForbiddenRejected("boards", """{ "id": "s", "mapId": "m", "boards": [], "placements": [] }""");
            AssertForbiddenRejected("nav", """{ "id": "s", "mapId": "m", "nav": {}, "placements": [] }""");
            AssertForbiddenRejected("navigation", """{ "id": "s", "mapId": "m", "navigation": {}, "placements": [] }""");
            AssertForbiddenRejected("pathing", """{ "id": "s", "mapId": "m", "pathing": {}, "placements": [] }""");
            AssertForbiddenRejected("collision", """{ "id": "s", "mapId": "m", "collision": {}, "placements": [] }""");
            AssertForbiddenRejected(
                "structureCollisionAsset",
                """{ "id": "s", "mapId": "m", "structureCollisionAsset": "x.bin", "placements": [] }""");
            AssertForbiddenRejected("templates", """{ "id": "s", "mapId": "m", "templates": [], "placements": [] }""");
            AssertForbiddenRejected("performers", """{ "id": "s", "mapId": "m", "performers": [], "placements": [] }""");
            AssertForbiddenRejected(
                "entityTemplates",
                """{ "id": "s", "mapId": "m", "entityTemplates": [], "placements": [] }""");
            AssertForbiddenRejected(
                "performerDefinitions",
                """{ "id": "s", "mapId": "m", "performerDefinitions": [], "placements": [] }""");
            AssertForbiddenRejected("entities", """{ "id": "s", "mapId": "m", "entities": [], "placements": [] }""");
            AssertForbiddenRejected("defaultCamera", """{ "id": "s", "mapId": "m", "defaultCamera": {}, "placements": [] }""");
            AssertForbiddenRejected("triggerTypes", """{ "id": "s", "mapId": "m", "triggerTypes": [], "placements": [] }""");
            AssertForbiddenRejected("metadata", """{ "id": "s", "mapId": "m", "metadata": {}, "placements": [] }""");
            AssertForbiddenRejected("ruleset", """{ "id": "s", "mapId": "m", "ruleset": {}, "placements": [] }""");
        }

        [Test]
        public void Load_RejectsBlankOrUntrimmedIdAndMapId()
        {
            AssertRejects(
                """[{ "id": "", "mapId": "maps/arena", "placements": [] }]""",
                expectedSubstring: "must define non-empty string field 'id'");

            AssertRejects(
                """[{ "id": "  padded.id  ", "mapId": "maps/arena", "placements": [] }]""",
                expectedSubstring: "must not include leading or trailing whitespace");

            AssertRejects(
                """[{ "id": "scenario.blank.map", "mapId": "", "placements": [] }]""",
                expectedSubstring: "mapId");

            AssertRejects(
                """[{ "id": "scenario.blank.map", "mapId": "  maps/arena  ", "placements": [] }]""",
                expectedSubstring: "must not include leading or trailing whitespace");
        }

        [Test]
        public void Load_RejectsBlankPlacementIdAndTemplateId()
        {
            AssertRejects(
                """
                [
                  {
                    "id": "scenario.blank.placement",
                    "mapId": "maps/arena",
                    "placements": [
                      { "id": "", "templateId": "unit.warrior" }
                    ]
                  }
                ]
                """,
                expectedSubstring: "placements[0].id");

            AssertRejects(
                """
                [
                  {
                    "id": "scenario.blank.template",
                    "mapId": "maps/arena",
                    "placements": [
                      { "id": "spawn.a", "templateId": "   " }
                    ]
                  }
                ]
                """,
                expectedSubstring: "templateId");
        }

        [Test]
        public void Load_RejectsInvalidOwnershipClosure()
        {
            AssertRejects(
                """
                [
                  {
                    "id": "scenario.bad.team",
                    "mapId": "maps/arena",
                    "placements": [{ "id": "spawn.a", "templateId": "unit.warrior", "teamId": 1 }],
                    "teams": [
                      { "teamId": 1, "representativePlacementId": "spawn.a" },
                      { "teamId": 1, "representativePlacementId": "spawn.a" }
                    ]
                  }
                ]
                """,
                expectedSubstring: "duplicate team id 1");

            AssertRejects(
                """
                [
                  {
                    "id": "scenario.unknown.rep",
                    "mapId": "maps/arena",
                    "placements": [{ "id": "spawn.a", "templateId": "unit.warrior", "teamId": 1 }],
                    "teams": [{ "teamId": 1, "representativePlacementId": "spawn.missing" }]
                  }
                ]
                """,
                expectedSubstring: "unknown placement id 'spawn.missing'");

            AssertRejects(
                """
                [
                  {
                    "id": "scenario.bad.player.team",
                    "mapId": "maps/arena",
                    "placements": [{ "id": "spawn.a", "templateId": "unit.warrior", "teamId": 1, "playerOwnerId": 7 }],
                    "teams": [{ "teamId": 1, "representativePlacementId": "spawn.a" }],
                    "players": [{ "playerId": 7, "teamId": 2, "representativePlacementId": "spawn.a" }]
                  }
                ]
                """,
                expectedSubstring: "unknown team id 2");

            AssertRejects(
                """
                [
                  {
                    "id": "scenario.bad.placement.player",
                    "mapId": "maps/arena",
                    "placements": [{ "id": "spawn.a", "templateId": "unit.warrior", "teamId": 1, "playerOwnerId": 7 }],
                    "teams": [{ "teamId": 1, "representativePlacementId": "spawn.a" }]
                  }
                ]
                """,
                expectedSubstring: "unknown player id 7");

            AssertRejects(
                """
                [
                  {
                    "id": "scenario.bad.relationship",
                    "mapId": "maps/arena",
                    "placements": [{ "id": "spawn.a", "templateId": "unit.warrior", "teamId": 1, "playerOwnerId": 7 }],
                    "teams": [{ "teamId": 1, "representativePlacementId": "spawn.a" }],
                    "players": [{ "playerId": 7, "teamId": 1, "representativePlacementId": "spawn.a" }],
                    "initialRelationships": {
                      "teams": [{ "teamA": 1, "teamB": 99, "typeId": "Relationship.Hostile" }]
                    }
                  }
                ]
                """,
                expectedSubstring: "unknown team id 99");
        }

        [Test]
        public void Load_RejectsInvalidPerformerParamOverrideValues()
        {
            AssertRejects(
                """
                [
                  {
                    "id": "scenario.bad.param.float",
                    "mapId": "maps/arena",
                    "placements": [
                      {
                        "id": "spawn.a",
                        "templateId": "unit.warrior",
                        "performerParamOverrides": [{ "paramKey": "tint.r", "lane": "Float" }]
                      }
                    ]
                  }
                ]
                """,
                expectedSubstring: "floatValue");

            AssertRejects(
                """
                [
                  {
                    "id": "scenario.bad.param.extra",
                    "mapId": "maps/arena",
                    "placements": [
                      {
                        "id": "spawn.a",
                        "templateId": "unit.warrior",
                        "performerParamOverrides": [{ "paramKey": "tint.r", "lane": "Float", "floatValue": 1, "intValue": 1 }]
                      }
                    ]
                  }
                ]
                """,
                expectedSubstring: "must not declare 'intValue'");

            AssertRejects(
                """
                [
                  {
                    "id": "scenario.bad.param.vector",
                    "mapId": "maps/arena",
                    "placements": [
                      {
                        "id": "spawn.a",
                        "templateId": "unit.warrior",
                        "performerParamOverrides": [{ "paramKey": "marker.color", "lane": "Vector", "vectorValue": [1, 0, 0] }]
                      }
                    ]
                  }
                ]
                """,
                expectedSubstring: "requires four numeric values");

            AssertRejects(
                """
                [
                  {
                    "id": "scenario.bad.param.lane",
                    "mapId": "maps/arena",
                    "placements": [
                      {
                        "id": "spawn.a",
                        "templateId": "unit.warrior",
                        "performerParamOverrides": [{ "paramKey": "tint.r", "lane": "float", "floatValue": 1 }]
                      }
                    ]
                  }
                ]
                """,
                expectedSubstring: "unsupported param lane 'float'");
        }

        private static void AssertForbiddenRejected(string fieldName, string planObjectJson)
        {
            string root = CreateTempRoot("scenario-plan-forbidden-" + fieldName);
            try
            {
                WriteCatalog(root);
                WritePlans(root, "[\n" + planObjectJson + "\n]");

                InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => Load(root));
                Assert.That(ex!.Message, Does.Contain($"forbidden field '{fieldName}'"));
                Assert.That(ex.Message, Does.Contain("not ScenarioPlan").Or.Contain("Ruleset/Profile"));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        private static void AssertRejects(string plansJson, string expectedSubstring)
        {
            string root = CreateTempRoot("scenario-plan-reject");
            try
            {
                WriteCatalog(root);
                WritePlans(root, plansJson);

                InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => Load(root));
                Assert.That(ex!.Message, Does.Contain(expectedSubstring));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        private static DataRegistry<ScenarioPlan> Load(string root)
        {
            ConfigPipeline pipeline = CreatePipeline(root);
            ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
            return new ScenarioPlanConfigLoader(pipeline).Load(catalog);
        }

        private static ConfigPipeline CreatePipeline(string root)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", root);
            return new ConfigPipeline(vfs, modLoader: null!);
        }

        private static void WriteCatalog(string root)
        {
            Directory.CreateDirectory(Path.Combine(root, "Configs"));
            File.WriteAllText(
                Path.Combine(root, "Configs", "config_catalog.json"),
                """
                [
                  { "Path": "Scenarios/scenario_plans.json", "Policy": "ArrayById", "IdField": "id" }
                ]
                """);
        }

        private static void WritePlans(string root, string json)
        {
            Directory.CreateDirectory(Path.Combine(root, "Configs", "Scenarios"));
            File.WriteAllText(Path.Combine(root, "Configs", "Scenarios", "scenario_plans.json"), json);
        }

        private static string CreateTempRoot(string prefix)
        {
            string root = Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                string candidate = Path.Combine(dir.FullName, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
        }

        private static void TryDeleteDirectory(string root)
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup for temp fixtures.
            }
        }
    }
}
