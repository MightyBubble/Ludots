using System;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Config;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using NUnit.Framework;
using static NUnit.Framework.Assert;
namespace Ludots.Tests.GAS
{
    public class EffectTemplateLoaderTests
    {
        [SetUp]
        public void SetUp()
        {
            EffectParamKeys.Initialize();
        }

        [Test]
        public void Load_EffectsJson_RegistersTemplatesAndResolvesCallbacks()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect_A",
                        "tags": ["Event.TestA"],
                        "presetType": "None",
                        "lifetime": "Instant",
                        "participatesInResponse": true,
                        "modifiers": [
                          { "attribute": "Health", "op": "Add", "value": -5 }
                        ]
                      },
                      {
                        "id": "Effect_B",
                        "tags": ["Event.TestB"],
                        "presetType": "None",
                        "lifetime": "After",
                        "duration": { "durationTicks": 10, "periodTicks": 0, "clockId": "FixedFrame" },
                        "participatesInResponse": true
                      }
                    ]
                    """);

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
                var pipeline = new ConfigPipeline(vfs, modLoader);

                var registry = new EffectTemplateRegistry();
                var loader = new EffectTemplateLoader(pipeline, registry);
                loader.Load(CreateEffectsCatalog(), relativePath: "GAS/effects.json");

                That(EffectTemplateIdRegistry.GetId("Effect_A"), Is.EqualTo(1));
                That(EffectTemplateIdRegistry.GetId("Effect_B"), Is.EqualTo(2));

                That(registry.TryGet(1, out var a), Is.True);
                That(registry.TryGet(2, out var b), Is.True);

                That(a.TagId, Is.Not.EqualTo(0));
                That(a.Modifiers.Count, Is.EqualTo(1));

                // TODO: b.OnApplyEffectId assertion removed — callback fields migrated to Phase Graph architecture
                // NOTE: JSON "onApplyEffect" field may also need updating in the loader
                That(b.LifetimeKind, Is.EqualTo(EffectLifetimeKind.After));
                That(b.DurationTicks, Is.GreaterThan(0));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_SecondsFields_AreRejected()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect_A",
                        "tags": ["Event.TestA"],
                        "duration": 1.0
                      }
                    ]
                    """);

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
                var pipeline = new ConfigPipeline(vfs, modLoader);

                var registry = new EffectTemplateRegistry();
                var loader = new EffectTemplateLoader(pipeline, registry);

                Throws<InvalidOperationException>(() => loader.Load(CreateEffectsCatalog(), relativePath: "GAS/effects.json"));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_MultipleTags_AreRejected()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect.MultipleTags",
                        "tags": ["Effect.First", "Effect.Second"],
                        "presetType": "None",
                        "lifetime": "Instant",
                        "participatesInResponse": false
                      }
                    ]
                    """);

                var loader = CreateLoader(root, out _);
                var ex = Throws<InvalidOperationException>(() =>
                    loader.Load(CreateEffectsCatalog(), relativePath: "GAS/effects.json"));

                That(ex!.Message, Does.Contain("tags"));
                That(ex.Message, Does.Contain("at most one"));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_TargetFilterWithoutRelationFilter_IsRejected()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect_Search",
                        "tags": ["Event.Search"],
                        "presetType": "Search",
                        "lifetime": "Instant",
                        "participatesInResponse": true,
                        "targetQuery": {
                          "kind": "BuiltinSpatial",
                          "shape": "Circle",
                          "radius": 100,
                          "innerRadius": 0,
                          "halfAngle": 0,
                          "halfWidth": 0,
                          "halfHeight": 0,
                          "rotation": 0,
                          "length": 0,
                          "graphProgramId": 0
                        },
                        "targetFilter": { "excludeSource": true, "maxTargets": 4 }
                      }
                    ]
                    """);

                var loader = CreateLoader(root, out _);

                Throws<InvalidOperationException>(() => loader.Load(CreateEffectsCatalog(), relativePath: "GAS/effects.json"));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_BuiltinSpatialCircle_RequiresOnlyRadius()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect_Search",
                        "tags": ["Event.Search"],
                        "presetType": "Search",
                        "lifetime": "Instant",
                        "participatesInResponse": true,
                        "targetQuery": {
                          "kind": "BuiltinSpatial",
                          "shape": "Circle",
                          "radius": 100
                        },
                        "targetFilter": {
                          "relationFilter": "All",
                          "excludeSource": true,
                          "maxTargets": 4
                        }
                      }
                    ]
                    """);

                var loader = CreateLoader(root, out var registry);

                loader.Load(CreateEffectsCatalog(), relativePath: "GAS/effects.json");

                int tplId = EffectTemplateIdRegistry.GetId("Effect_Search");
                That(tplId, Is.GreaterThan(0));
                That(registry.TryGet(tplId, out var tpl), Is.True);
                That(tpl.TargetQuery.Kind, Is.EqualTo(TargetResolverKind.BuiltinSpatial));
                That(tpl.TargetQuery.Spatial.Shape, Is.EqualTo(SpatialShape.Circle));
                That(tpl.TargetQuery.Spatial.RadiusCm, Is.EqualTo(100));
                That(tpl.TargetQuery.Spatial.InnerRadiusCm, Is.EqualTo(0));
                That(tpl.TargetQuery.Spatial.HalfAngleDeg, Is.EqualTo(0));
                That(tpl.TargetQuery.Spatial.HalfWidthCm, Is.EqualTo(0));
                That(tpl.TargetQuery.Spatial.HalfHeightCm, Is.EqualTo(0));
                That(tpl.TargetQuery.Spatial.RotationDeg, Is.EqualTo(0));
                That(tpl.TargetQuery.Spatial.LengthCm, Is.EqualTo(0));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_ProjectileWithoutCollisionRelationFilter_IsRejected()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect_Projectile",
                        "tags": ["Event.Projectile"],
                        "presetType": "LaunchProjectile",
                        "lifetime": "Instant",
                        "participatesInResponse": true,
                        "projectile": {
                          "speed": 1000,
                          "range": 1200,
                          "arcHeight": 0,
                          "impactEffect": "Effect_Hit",
                          "hitEffect": "Effect_Hit",
                          "presentationEffect": "Effect_Hit",
                          "travelMode": "Direction",
                          "impactPolicy": "DestroyOnFirstHit",
                          "collisionHalfWidth": 24,
                          "collisionExcludeSource": true,
                          "maxHitCount": 0
                        }
                      },
                      {
                        "id": "Effect_Hit",
                        "tags": ["Event.Hit"],
                        "presetType": "InstantDamage",
                        "lifetime": "Instant",
                        "participatesInResponse": true
                      }
                    ]
                    """);

                var loader = CreateLoader(root, out _);

                Throws<InvalidOperationException>(() => loader.Load(CreateEffectsCatalog(), relativePath: "GAS/effects.json"));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [TestCase(0)]
        [TestCase(ProjectileState.HitHistoryCapacity + 1)]
        public void Load_ProjectileMaxHitCountOutsideFixedHistory_IsRejected(int invalidMaxHitCount)
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    $$"""
                    [
                      {
                        "id": "Effect_Projectile",
                        "tags": ["Event.Projectile"],
                        "presetType": "LaunchProjectile",
                        "lifetime": "Instant",
                        "participatesInResponse": true,
                        "projectile": {
                          "speed": 1000,
                          "range": 1200,
                          "arcHeight": 0,
                          "hitEffect": "Effect_Projectile",
                          "travelMode": "Direction",
                          "impactPolicy": "ContinueOnHit",
                          "collisionHalfWidth": 24,
                          "collisionRelationFilter": "All",
                          "collisionExcludeSource": true,
                          "maxHitCount": {{invalidMaxHitCount}}
                        }
                      }
                    ]
                    """);

                var loader = CreateLoader(root, out _);

                InvalidOperationException ex = Throws<InvalidOperationException>(() =>
                    loader.Load(CreateEffectsCatalog(), relativePath: "GAS/effects.json"))!;
                That(ex.Message, Does.Contain("projectile.maxHitCount"));
                That(ex.Message, Does.Contain($"1..{ProjectileState.HitHistoryCapacity}"));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_LegacyProjectileMode_IsRejectedWithMigrationGuidance()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect_Projectile",
                        "tags": ["Event.Projectile"],
                        "presetType": "LaunchProjectile",
                        "lifetime": "Instant",
                        "participatesInResponse": true,
                        "projectile": {
                          "speed": 1000,
                          "range": 1200,
                          "arcHeight": 0,
                          "travelMode": "Legacy",
                          "impactPolicy": "Legacy"
                        }
                      }
                    ]
                    """);

                var loader = CreateLoader(root, out _);
                InvalidOperationException ex = Throws<InvalidOperationException>(() =>
                    loader.Load(CreateEffectsCatalog(), relativePath: "GAS/effects.json"))!;

                That(ex.Message, Does.Contain("travelMode 'Legacy' was removed"));
                That(ex.Message, Does.Contain("Direction"));
                That(ex.Message, Does.Contain("TrackTarget"));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_ProjectileBlankOptionalEffectRef_IsRejected()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect_Projectile",
                        "tags": ["Event.Projectile"],
                        "presetType": "LaunchProjectile",
                        "lifetime": "Instant",
                        "participatesInResponse": true,
                        "projectile": {
                          "speed": 1000,
                          "range": 1200,
                          "arcHeight": 0,
                          "impactEffect": "",
                          "travelMode": "Direction",
                          "impactPolicy": "DestroyOnFirstHit",
                          "hitEffect": "Effect_Projectile",
                          "collisionHalfWidth": 24,
                          "collisionRelationFilter": "All"
                        }
                      }
                    ]
                    """);

                var loader = CreateLoader(root, out _);

                var ex = Throws<InvalidOperationException>(() => loader.Load(CreateEffectsCatalog(), relativePath: "GAS/effects.json"));

                That(ex!.Message, Does.Contain("projectile.impactEffect"));
                That(ex.Message, Does.Contain("omitted or a semantic key"));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_ProjectileMissingHitEffect_IsRejected()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect_Projectile",
                        "tags": ["Event.Projectile"],
                        "presetType": "LaunchProjectile",
                        "lifetime": "Instant",
                        "participatesInResponse": true,
                        "projectile": {
                          "speed": 1000,
                          "range": 1200,
                          "arcHeight": 0,
                          "travelMode": "Direction",
                          "impactPolicy": "DestroyOnFirstHit",
                          "collisionHalfWidth": 24,
                          "collisionRelationFilter": "All"
                        }
                      }
                    ]
                    """);

                var loader = CreateLoader(root, out _);

                var ex = Throws<InvalidOperationException>(() => loader.Load(CreateEffectsCatalog(), relativePath: "GAS/effects.json"));

                That(ex!.Message, Does.Contain("projectile.hitEffect"));
                That(ex.Message, Does.Contain("DestroyOnFirstHit"));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_InfiniteLifetimeCanOmitDurationBlock()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect_Aura",
                        "tags": ["Event.Aura"],
                        "presetType": "None",
                        "lifetime": "Infinite",
                        "participatesInResponse": true
                      }
                    ]
                    """);

                var loader = CreateLoader(root, out var registry);
                loader.Load(CreateEffectsCatalog(), relativePath: "GAS/effects.json");

                int templateId = EffectTemplateIdRegistry.GetId("Effect_Aura");
                That(registry.TryGet(templateId, out var tpl), Is.True);
                That(tpl.LifetimeKind, Is.EqualTo(EffectLifetimeKind.Infinite));
                That(tpl.DurationTicks, Is.EqualTo(0));
                That(tpl.PeriodTicks, Is.EqualTo(0));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_InfiniteLifetimeDefaultZeroDurationBlock_IsRejected()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect_Aura",
                        "tags": ["Event.Aura"],
                        "presetType": "None",
                        "lifetime": "Infinite",
                        "duration": { "durationTicks": 0, "periodTicks": 0, "clockId": "FixedFrame" },
                        "participatesInResponse": true
                      }
                    ]
                    """);

                var loader = CreateLoader(root, out _);

                var ex = Throws<InvalidOperationException>(() => loader.Load(CreateEffectsCatalog(), relativePath: "GAS/effects.json"));

                That(ex!.Message, Does.Contain("duration"));
                That(ex.Message, Does.Contain("omit"));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_TargetDispatchPayloadEffectWithoutContextMapping_UsesDefaultMapping()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect_Search",
                        "tags": ["Event.Search"],
                        "presetType": "Search",
                        "lifetime": "Instant",
                        "participatesInResponse": true,
                        "targetQuery": {
                          "kind": "BuiltinSpatial",
                          "shape": "Circle",
                          "radius": 100
                        },
                        "targetFilter": { "excludeSource": true, "maxTargets": 4, "relationFilter": "All" },
                        "targetDispatch": { "payloadEffect": "Effect_Hit" }
                      },
                      {
                        "id": "Effect_Hit",
                        "tags": ["Event.Hit"],
                        "presetType": "InstantDamage",
                        "lifetime": "Instant",
                        "participatesInResponse": true
                      }
                    ]
                    """);

                var loader = CreateLoader(root, out var registry);
                loader.Load(CreateEffectsCatalog(), relativePath: "GAS/effects.json");

                int templateId = EffectTemplateIdRegistry.GetId("Effect_Search");
                int hitId = EffectTemplateIdRegistry.GetId("Effect_Hit");
                That(registry.TryGet(templateId, out var tpl), Is.True);
                That(tpl.TargetDispatch.PayloadEffectTemplateId, Is.EqualTo(hitId));
                That(tpl.TargetDispatch.ContextMapping.PayloadSource, Is.EqualTo(ContextSlot.OriginalSource));
                That(tpl.TargetDispatch.ContextMapping.PayloadTarget, Is.EqualTo(ContextSlot.ResolvedEntity));
                That(tpl.TargetDispatch.ContextMapping.PayloadTargetContext, Is.EqualTo(ContextSlot.OriginalTarget));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_PhaseListenerOmittedPriority_DefaultsToZero()
        {
            GraphIdRegistry.Clear();
            GraphIdRegistry.Register("Graph.Test.OnHit");

            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect_Reactive",
                        "tags": ["Event.Reactive"],
                        "presetType": "None",
                        "lifetime": "Infinite",
                        "participatesInResponse": true,
                        "phaseListeners": [
                          {
                            "phase": "OnHit",
                            "scope": "Target",
                            "action": "Graph",
                            "graphProgram": "Graph.Test.OnHit"
                          }
                        ]
                      }
                    ]
                    """);

                var loader = CreateLoader(root, out var registry);
                loader.Load(CreateEffectsCatalog(), relativePath: "GAS/effects.json");

                int templateId = EffectTemplateIdRegistry.GetId("Effect_Reactive");
                That(registry.TryGet(templateId, out var tpl), Is.True);
                That(tpl.ListenerSetup.Count, Is.EqualTo(1));
                unsafe
                {
                    That(tpl.ListenerSetup.Priorities[0], Is.EqualTo(0));
                }
            }
            finally
            {
                GraphIdRegistry.Clear();
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_InstantPhaseListener_RejectsCrossFrameOwnership()
        {
            GraphIdRegistry.Clear();
            GraphIdRegistry.Register("Graph.Test.OnHit");
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect.Invalid.InstantListener",
                        "tags": ["Event.Invalid"],
                        "presetType": "None",
                        "lifetime": "Instant",
                        "participatesInResponse": false,
                        "phaseListeners": [
                          {
                            "phase": "OnHit",
                            "scope": "Target",
                            "action": "Graph",
                            "graphProgram": "Graph.Test.OnHit"
                          }
                        ]
                      }
                    ]
                    """);

                var loader = CreateLoader(root, out _);
                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                    loader.Load(CreateEffectsCatalog(), relativePath: "GAS/effects.json"))!;

                That(ex.Message, Does.Contain("lifetime Instant cannot declare phaseListeners"));
            }
            finally
            {
                GraphIdRegistry.Clear();
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_ScatterUnitCreationOmittedOffsetRadius_IsAcceptedAsZero()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect_CreateUnit",
                        "tags": ["Event.CreateUnit"],
                        "presetType": "CreateUnit",
                        "lifetime": "Instant",
                        "participatesInResponse": true,
                        "unitCreation": {
                          "unitType": "Unit.Test.Wolf",
                          "count": 1,
                          "placementPattern": "Scatter"
                        }
                      }
                    ]
                    """);

                var loader = CreateLoader(root, out var registry);
                loader.Load(CreateEffectsCatalog(), relativePath: "GAS/effects.json");

                int templateId = EffectTemplateIdRegistry.GetId("Effect_CreateUnit");
                That(registry.TryGet(templateId, out var tpl), Is.True);
                That(tpl.UnitCreation.PlacementPattern, Is.EqualTo(UnitCreationPlacementPattern.Scatter));
                That(tpl.UnitCreation.OffsetRadius, Is.EqualTo(0));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_ScatterUnitCreationNegativeOffsetRadius_IsRejected()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect_CreateUnit",
                        "tags": ["Event.CreateUnit"],
                        "presetType": "CreateUnit",
                        "lifetime": "Instant",
                        "participatesInResponse": true,
                        "unitCreation": {
                          "unitType": "Unit.Test.Wolf",
                          "count": 1,
                          "placementPattern": "Scatter",
                          "offsetRadius": -1
                        }
                      }
                    ]
                    """);

                var loader = CreateLoader(root, out _);

                var ex = Throws<InvalidOperationException>(() => loader.Load(CreateEffectsCatalog(), relativePath: "GAS/effects.json"));

                That(ex!.Message, Does.Contain("unitCreation.offsetRadius"));
                That(ex.Message, Does.Contain("non-negative"));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_CircleUnitCreationOffsetRadius_IsRejected()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect_CreateUnit",
                        "tags": ["Event.CreateUnit"],
                        "presetType": "CreateUnit",
                        "lifetime": "Instant",
                        "participatesInResponse": true,
                        "unitCreation": {
                          "unitType": "Unit.Test.Wolf",
                          "count": 3,
                          "placementPattern": "Circle",
                          "offsetRadius": 80,
                          "placementRadiusCm": 160,
                          "placementStartAngleDeg": 0
                        }
                      }
                    ]
                    """);

                var loader = CreateLoader(root, out _);

                var ex = Throws<InvalidOperationException>(() => loader.Load(CreateEffectsCatalog(), relativePath: "GAS/effects.json"));

                That(ex!.Message, Does.Contain("unitCreation.offsetRadius"));
                That(ex.Message, Does.Contain("placementPattern=Circle"));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_GrantedTagsGraphProgramFormula_IsRejectedUntilEvaluatorIsWired()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect_GraphTag",
                        "tags": ["Event.GraphTag"],
                        "presetType": "None",
                        "lifetime": "Infinite",
                        "participatesInResponse": true,
                        "grantedTags": [
                          {
                            "tag": "Status.GraphDriven",
                            "formula": "GraphProgram",
                            "graphProgram": "Graph.TagContribution"
                          }
                        ]
                      }
                    ]
                    """);

                var loader = CreateLoader(root, out _);

                var ex = Throws<InvalidOperationException>(() => loader.Load(CreateEffectsCatalog(), relativePath: "GAS/effects.json"));

                That(ex!.Message, Does.Contain("formula=GraphProgram"));
                That(ex.Message, Does.Contain("tag contribution graph evaluator"));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_PresetType_ApplyForce2D_CompilesPresetFields()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect_Preset_Force",
                        "tags": ["Effect.ApplyForce"],
                        "presetType": "ApplyForce2D",
                        "lifetime": "Instant",
                        "participatesInResponse": true,
                        "configParams": {
                          "_ep.forceXTargetAttrId": { "type": "Attribute", "value": "Physics.ForceRequestX" },
                          "_ep.forceYTargetAttrId": { "type": "Attribute", "value": "Physics.ForceRequestY" }
                        }
                      }
                    ]
                    """);

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
                var pipeline = new ConfigPipeline(vfs, modLoader);

                var registry = new EffectTemplateRegistry();
                var loader = new EffectTemplateLoader(pipeline, registry);
                loader.Load(CreateEffectsCatalog(), relativePath: "GAS/effects.json");

                int tplId = EffectTemplateIdRegistry.GetId("Effect_Preset_Force");
                That(tplId, Is.GreaterThan(0));
                That(registry.TryGet(tplId, out var tpl), Is.True);
                That(tpl.PresetType, Is.EqualTo(EffectPresetType.ApplyForce2D));
                That(tpl.PresetAttribute0, Is.GreaterThanOrEqualTo(0));
                That(tpl.PresetAttribute1, Is.GreaterThanOrEqualTo(0));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_GraphProgramTargetQuery_RequiresGraphProgramId()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect_Graph_Query",
                        "tags": ["Event.Search"],
                        "presetType": "Search",
                        "lifetime": "Instant",
                        "participatesInResponse": true,
                        "targetQuery": {
                          "kind": "GraphProgram"
                        },
                        "targetFilter": {
                          "relationFilter": "All",
                          "excludeSource": true,
                          "maxTargets": 4
                        }
                      }
                    ]
                    """);

                var loader = CreateLoader(root, out _);

                Throws<InvalidOperationException>(() => loader.Load(CreateEffectsCatalog(), relativePath: "GAS/effects.json"));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_SubmitOrderFromBlackboard_RequiresEntityOrderIntArg0()
        {
            string root = CreateTempRoot();
            try
            {
                OrderBlackboardKeyRegistry.ResetToBuiltins();
                OrderBlackboardKeyRegistry.Register("Test.SpawnTarget.Kind");
                OrderBlackboardKeyRegistry.Register("Test.SpawnTarget.Position");
                OrderBlackboardKeyRegistry.Register("Test.SpawnTarget.Entity");
                OrderBlackboardKeyRegistry.Register("Test.SpawnTarget.HexQ");
                OrderBlackboardKeyRegistry.Register("Test.SpawnTarget.HexR");

                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect.Test.SubmitOrderMissingIntArg0",
                        "tags": ["Effect.Test.SubmitOrder"],
                        "presetType": "SubmitOrderFromBlackboard",
                        "lifetime": "Instant",
                        "participatesInResponse": false,
                        "submitOrderFromBlackboard": {
                          "source": "Source",
                          "target": "Target",
                          "storedTarget": {
                            "targetKindKey": "Test.SpawnTarget.Kind",
                            "targetPositionKey": "Test.SpawnTarget.Position",
                            "targetEntityKey": "Test.SpawnTarget.Entity",
                            "hexQKey": "Test.SpawnTarget.HexQ",
                            "hexRKey": "Test.SpawnTarget.HexR"
                          },
                          "pointMoveOrderTypeKey": "moveTo",
                          "entityOrderTypeKey": "castAbility",
                          "submitMode": "Immediate"
                        }
                      }
                    ]
                    """);

                var loader = CreateLoader(root, out _, CreateOrderTypes());
                var ex = Throws<InvalidOperationException>(() =>
                    loader.Load(CreateEffectsCatalog(), relativePath: "GAS/effects.json"));
                That(ex!.Message, Does.Contain("submitOrderFromBlackboard.entityOrderIntArg0"));
            }
            finally
            {
                OrderBlackboardKeyRegistry.ResetToBuiltins();
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_SubmitOrderFromBlackboard_UnknownOrderTypeKeyIsRejected()
        {
            string root = CreateTempRoot();
            try
            {
                OrderBlackboardKeyRegistry.ResetToBuiltins();
                OrderBlackboardKeyRegistry.Register("Test.SpawnTarget.Kind");
                OrderBlackboardKeyRegistry.Register("Test.SpawnTarget.Position");
                OrderBlackboardKeyRegistry.Register("Test.SpawnTarget.Entity");
                OrderBlackboardKeyRegistry.Register("Test.SpawnTarget.HexQ");
                OrderBlackboardKeyRegistry.Register("Test.SpawnTarget.HexR");

                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect.Test.SubmitOrderUnknownType",
                        "tags": ["Effect.Test.SubmitOrder"],
                        "presetType": "SubmitOrderFromBlackboard",
                        "lifetime": "Instant",
                        "participatesInResponse": false,
                        "submitOrderFromBlackboard": {
                          "source": "Source",
                          "target": "Target",
                          "storedTarget": {
                            "targetKindKey": "Test.SpawnTarget.Kind",
                            "targetPositionKey": "Test.SpawnTarget.Position",
                            "targetEntityKey": "Test.SpawnTarget.Entity",
                            "hexQKey": "Test.SpawnTarget.HexQ",
                            "hexRKey": "Test.SpawnTarget.HexR"
                          },
                          "pointMoveOrderTypeKey": "moveTypo",
                          "entityOrderTypeKey": "castAbility",
                          "entityOrderIntArg0": 1,
                          "submitMode": "Immediate"
                        }
                      }
                    ]
                    """);

                var loader = CreateLoader(root, out _, CreateOrderTypes());
                var ex = Throws<InvalidOperationException>(() =>
                    loader.Load(CreateEffectsCatalog(), relativePath: "GAS/effects.json"));

                That(ex!.Message, Does.Contain("pointMoveOrderTypeKey"));
                That(ex.Message, Does.Contain("moveTypo"));
                That(ex.Message, Does.Contain("unknown order type"));
            }
            finally
            {
                OrderBlackboardKeyRegistry.ResetToBuiltins();
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_InfiniteDuration_AllowsMissingAndPartialDurationBlocks()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect_Infinite_NoDuration",
                        "tags": ["Event.Infinite.NoDuration"],
                        "presetType": "Buff",
                        "lifetime": "Infinite",
                        "participatesInResponse": false
                      },
                      {
                        "id": "Effect_Infinite_PeriodOnly",
                        "tags": ["Event.Infinite.PeriodOnly"],
                        "presetType": "Buff",
                        "lifetime": "Infinite",
                        "duration": { "periodTicks": 20 },
                        "participatesInResponse": false
                      },
                      {
                        "id": "Effect_Infinite_FullDuration",
                        "tags": ["Event.Infinite.FullDuration"],
                        "presetType": "Buff",
                        "lifetime": "Infinite",
                        "duration": { "durationTicks": 0, "periodTicks": 60, "clockId": "FixedFrame" },
                        "participatesInResponse": false
                      }
                    ]
                    """);

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
                var pipeline = new ConfigPipeline(vfs, modLoader);

                var registry = new EffectTemplateRegistry();
                var loader = new EffectTemplateLoader(pipeline, registry);
                loader.Load(CreateEffectsCatalog(), relativePath: "GAS/effects.json");

                int noDurationId = EffectTemplateIdRegistry.GetId("Effect_Infinite_NoDuration");
                int periodOnlyId = EffectTemplateIdRegistry.GetId("Effect_Infinite_PeriodOnly");
                int fullDurationId = EffectTemplateIdRegistry.GetId("Effect_Infinite_FullDuration");

                That(noDurationId, Is.GreaterThan(0));
                That(periodOnlyId, Is.GreaterThan(0));
                That(fullDurationId, Is.GreaterThan(0));

                That(registry.TryGet(noDurationId, out var noDuration), Is.True);
                That(registry.TryGet(periodOnlyId, out var periodOnly), Is.True);
                That(registry.TryGet(fullDurationId, out var fullDuration), Is.True);

                That(noDuration.LifetimeKind, Is.EqualTo(EffectLifetimeKind.Infinite));
                That(noDuration.DurationTicks, Is.EqualTo(0));
                That(noDuration.PeriodTicks, Is.EqualTo(0));
                That(noDuration.ClockId, Is.EqualTo(GasClockId.FixedFrame));

                That(periodOnly.LifetimeKind, Is.EqualTo(EffectLifetimeKind.Infinite));
                That(periodOnly.DurationTicks, Is.EqualTo(0));
                That(periodOnly.PeriodTicks, Is.EqualTo(20));
                That(periodOnly.ClockId, Is.EqualTo(GasClockId.FixedFrame));

                That(fullDuration.LifetimeKind, Is.EqualTo(EffectLifetimeKind.Infinite));
                That(fullDuration.DurationTicks, Is.EqualTo(0));
                That(fullDuration.PeriodTicks, Is.EqualTo(60));
                That(fullDuration.ClockId, Is.EqualTo(GasClockId.FixedFrame));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        private static string CreateTempRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_EffectTemplateLoaderTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static EffectTemplateLoader CreateLoader(
            string root,
            out EffectTemplateRegistry registry,
            OrderTypeRegistry? orderTypes = null)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", root);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);

            registry = new EffectTemplateRegistry();
            return new EffectTemplateLoader(pipeline, registry, orderTypes: orderTypes);
        }

        private static OrderTypeRegistry CreateOrderTypes()
        {
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig { Key = "moveTo", OrderTypeId = 101 });
            orderTypes.Register(new OrderTypeConfig { Key = "castAbility", OrderTypeId = 100 });
            return orderTypes;
        }

        private static ConfigCatalog CreateEffectsCatalog()
        {
            var catalog = new ConfigCatalog();
            catalog.Add(new ConfigCatalogEntry("GAS/effects.json", ConfigMergePolicy.ArrayById, "id"));
            return catalog;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
