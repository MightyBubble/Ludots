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
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    public class ProjectilePresentationBridgeTests
    {
        [Test]
        public void PerformerDefinitionConfigLoader_ResolvesProjectileSpawnedRuleKeys_AndSelfReferences()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs"));
                Directory.CreateDirectory(Path.Combine(root, "Configs", "Presentation"));
                File.WriteAllText(
                    Path.Combine(root, "Configs", "config_catalog.json"),
                    """
                    [
                      { "Path": "Presentation/performers.json", "Policy": "ArrayById", "IdField": "id" }
                    ]
                    """);
                File.WriteAllText(
                    Path.Combine(root, "Configs", "Presentation", "performers.json"),
                    """
                    [
                      {
                        "id": "test.projectile.performer",
                        "visualKind": "Marker3D",
                        "rules": [
                          {
                            "event": {
                              "kind": "ProjectileSpawned",
                              "key": "Effect.Test.ProjectileHit"
                            },
                            "command": {
                              "kind": "CreatePerformer",
                              "scopeSource": "EventPayloadA",
                              "definitionId": "test.projectile.performer"
                            }
                          },
                          {
                            "event": {
                              "kind": "EntityDestroyed"
                            },
                            "command": {
                              "kind": "DestroyPerformerScope",
                              "scopeSource": "EventPayloadA"
                            }
                          }
                        ]
                      }
                    ]
                    """);

                EffectTemplateIdRegistry.Clear();
                int impactEffectId = EffectTemplateIdRegistry.Register("Effect.Test.ProjectileHit");

                var performers = new PerformerDefinitionRegistry();

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
                var pipeline = new ConfigPipeline(vfs, modLoader);
                var catalog = ConfigCatalogLoader.Load(pipeline);

                var loader = new PerformerDefinitionConfigLoader(
                    pipeline,
                    performers,
                    resolveEffectTemplateId: EffectTemplateIdRegistry.GetId);

                loader.Load(catalog);

                int performerId = performers.GetId("test.projectile.performer");
                That(performerId, Is.GreaterThan(0));
                That(performers.TryGet(performerId, out var definition), Is.True);
                That(definition.Rules.Length, Is.EqualTo(2));
                That(definition.Rules[0].Event.Kind, Is.EqualTo(PresentationEventKind.ProjectileSpawned));
                That(definition.Rules[0].Event.KeyId, Is.EqualTo(impactEffectId));
                That(definition.Rules[0].Command.CommandKind, Is.EqualTo(PerformerCommandKind.CreatePerformer));
                That(definition.Rules[0].Command.ScopeSource, Is.EqualTo(PerformerCommandScopeSource.EventPayloadA));
                That(definition.Rules[0].Command.PerformerDefinitionId, Is.EqualTo(performerId));
                That(definition.Rules[1].Event.Kind, Is.EqualTo(PresentationEventKind.EntityDestroyed));
                That(definition.Rules[1].Command.CommandKind, Is.EqualTo(PerformerCommandKind.DestroyPerformerScope));
                That(definition.Rules[1].Command.ScopeSource, Is.EqualTo(PerformerCommandScopeSource.EventPayloadA));
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
