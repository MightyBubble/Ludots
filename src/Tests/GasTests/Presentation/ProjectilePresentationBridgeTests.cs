using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using NUnit.Framework;
using static NUnit.Framework.Assert;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.GAS
{
    public class ProjectilePresentationBridgeTests
    {
        [Test]
        public void PresenterDefinitionConfigLoader_ResolvesProjectileSpawnedRuleKeys_AndSelfReferences()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(root);
                Directory.CreateDirectory(Path.Combine(root, "Presentation"));
                File.WriteAllText(
                    Path.Combine(root, "config_catalog.json"),
                    """
                    [
                      { "Path": "Presentation/presenters.json", "Policy": "ArrayById", "IdField": "id" }
                    ]
                    """);
                File.WriteAllText(
                    Path.Combine(root, "Presentation", "presenters.json"),
                    """
                    [
                      {
                        "id": "test.projectile.presenter",
                        "behaviors": [
                          {
                            "slot": "body",
                            "kind": "AssetBinding",
                            "activeByDefault": true,
                            "assetBinding": {
                              "assetKind": "Mesh",
                              "assetId": "test.mesh",
                              "renderPath": "StaticMesh",
                              "mobility": "Movable",
                              "localScale": [1, 1, 1]
                            }
                          }
                        ],
                        "rules": [
                          {
                            "event": {
                              "kind": "ProjectileSpawned",
                              "key": "Effect.Test.ProjectileHit"
                            },
                            "command": {
                              "kind": "CreatePresenter",
                              "scopeSource": "EventPayloadA",
                              "definitionId": "test.projectile.presenter"
                            }
                          },
                          {
                            "event": {
                              "kind": "EntityDestroyed",
                              "key": "*"
                            },
                            "command": {
                              "kind": "DestroyPresenterScope",
                              "scopeSource": "EventPayloadA"
                            }
                          }
                        ]
                      }
                    ]
                    """);

                EffectTemplateIdRegistry.Clear();
                int impactEffectId = EffectTemplateIdRegistry.Register("Effect.Test.ProjectileHit");

                var presenters = new PresenterDefinitionRegistry();

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
                var pipeline = new ConfigPipeline(vfs, modLoader);
                var catalog = ConfigCatalogLoader.Load(pipeline);

                var loader = new PresenterDefinitionConfigLoader(
                    pipeline,
                    presenters,
                    resolveEffectTemplateId: EffectTemplateIdRegistry.GetId,
                    resolveBehaviorAssetId: (kind, key) => kind == AssetKind.Mesh && key == "test.mesh" ? 1 : 0);

                loader.Load(catalog);

                int presenterId = presenters.GetId("test.projectile.presenter");
                That(presenterId, Is.GreaterThan(0));
                That(presenters.TryGet(presenterId, out var definition), Is.True);
                That(definition.Rules.Length, Is.EqualTo(2));
                That(definition.Rules[0].Event.Kind, Is.EqualTo(PresentationEventKind.ProjectileSpawned));
                That(definition.Rules[0].Event.KeyId, Is.EqualTo(impactEffectId));
                That(definition.Rules[0].Command.CommandKind, Is.EqualTo(PresenterCommandKind.CreatePresenter));
                That(definition.Rules[0].Command.ScopeSource, Is.EqualTo(PresenterCommandScopeSource.EventPayloadA));
                That(definition.Rules[0].Command.PresenterDefinitionId, Is.EqualTo(presenterId));
                That(definition.Rules[1].Event.Kind, Is.EqualTo(PresentationEventKind.EntityDestroyed));
                That(definition.Rules[1].Command.CommandKind, Is.EqualTo(PresenterCommandKind.DestroyPresenterScope));
                That(definition.Rules[1].Command.ScopeSource, Is.EqualTo(PresenterCommandScopeSource.EventPayloadA));
            }
            finally
            {
                TryDeleteDirectory(root);
                EffectTemplateIdRegistry.Clear();
            }
        }

        [Test]
        public void ProjectilePresentationBootstrapSystem_EnsuresMinimalProjectilePresentationContract()
        {
            using var world = World.Create();
            int impactEffectId = 42;

            Entity projectile = world.Create(
                new ProjectileState
                {
                    ImpactEffectTemplateId = impactEffectId,
                },
                WorldPositionCm.FromCm(120, 240),
                new PreviousWorldPositionCm { Value = WorldPositionCm.FromCm(120, 240).Value });

            using var system = new ProjectilePresentationBootstrapSystem(
                world,
                new PresentationStableIdAllocator());

            system.Update(0f);

            That(world.Has<ProjectilePresentationBootstrapState>(projectile), Is.True);
            That(world.Has<PresentationStableId>(projectile), Is.True);
            That(world.Get<PresentationStableId>(projectile).Value, Is.GreaterThan(0));
            That(world.Has<VisualTransform>(projectile), Is.True);
            That(world.Has<CullState>(projectile), Is.True);
        }

        private static string CreateTempRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_ProjectilePresentationBridgeTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
