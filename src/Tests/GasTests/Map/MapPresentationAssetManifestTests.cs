using System;
using System.Collections.Generic;
using System.IO;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Map;
using Ludots.Core.Modding;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
public sealed class MapPresentationAssetManifestTests
{
    [Test]
    public void BuildPresentationAssetManifest_CollectsRecursiveTemplatesPresenterChildrenAndSwapAssets()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "Ludots_MapPresentationAssetManifestTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Entities"));

        try
        {
            File.WriteAllText(
                Path.Combine(root, "config_catalog.json"),
                """
                [
                  { "Path": "Entities/templates.json", "Policy": "ArrayById", "IdField": "id" }
                ]
                """);
            File.WriteAllText(
                Path.Combine(root, "Entities", "templates.json"),
                """
                [
                  {
                    "id": "manifest.root",
                    "components": { "Name": { "Value": "Manifest Root" } },
                    "children": [
                      {
                        "template": "manifest.child",
                        "localPose": {
                          "offsetXCm": 0,
                          "offsetYCm": 0,
                          "facingDeg": 0,
                          "inheritParentFacing": false,
                          "offsetRotation": "None"
                        }
                      }
                    ]
                  },
                  {
                    "id": "manifest.child",
                    "components": { "Name": { "Value": "Manifest Child" } }
                  }
                ]
                """);

            using World world = World.Create();
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", root);
            var pipeline = new ConfigPipeline(vfs, new ModLoader(vfs, new FunctionRegistry(), new TriggerManager()));
            var loader = new MapLoader(world, new WorldMap(), pipeline);
            loader.LoadTemplates(ConfigCatalogLoader.Load(pipeline));

            var meshes = new MeshAssetRegistry();
            int rootMeshId = meshes.Register(
                "manifest.root.mesh",
                MeshAssetDescriptor.Model(0, "mod:root.glb"));
            int swapMeshId = meshes.Register(
                "manifest.swap.mesh",
                MeshAssetDescriptor.Model(0, "mod:swap.glb"));
            int planChildMeshId = meshes.Register(
                "manifest.plan-child.mesh",
                MeshAssetDescriptor.Model(0, "mod:plan-child.glb"));
            int templateChildMeshId = meshes.Register(
                "manifest.template-child.mesh",
                MeshAssetDescriptor.Model(0, "mod:template-child.glb"));

            var definitions = new PresenterDefinitionRegistry();
            int planChildDefinitionId = definitions.Register(
                "manifest.plan-child",
                new PresenterDefinition
                {
                    Behaviors =
                    [
                        AssetBehavior(0, AssetKind.SkinnedMesh, planChildMeshId, VisualRenderPath.GpuSkinnedInstance),
                    ],
                });
            int rootDefinitionId = definitions.GetOrRegisterId("manifest.presenter-root");
            int rootTemplateKey = loader.EntityTemplateKeys.GetId("manifest.root");
            definitions.Register(
                "manifest.presenter-root",
                new PresenterDefinition
                {
                    Children = [new ChildPresenterRef { DefinitionId = planChildDefinitionId }],
                    Behaviors =
                    [
                        AssetBehavior(
                            0,
                            AssetKind.Mesh,
                            rootMeshId,
                            VisualRenderPath.StaticMesh,
                            [new AssetSwapEntry { ParamValue = 1f, AssetId = swapMeshId }]),
                        AssetBehavior(1, AssetKind.WorldHud, 999_999, VisualRenderPath.None),
                    ],
                    Rules = [CreateOnSpawn(rootTemplateKey, rootDefinitionId)],
                });

            int childTemplateKey = loader.EntityTemplateKeys.GetId("manifest.child");
            int childDefinitionId = definitions.GetOrRegisterId("manifest.presenter-template-child");
            definitions.Register(
                "manifest.presenter-template-child",
                new PresenterDefinition
                {
                    Behaviors =
                    [
                        AssetBehavior(0, AssetKind.Decal, templateChildMeshId, VisualRenderPath.StaticMesh),
                    ],
                    Rules = [CreateOnSpawn(childTemplateKey, childDefinitionId)],
                });

            loader.SetPresentationRuntime(
                new PresentationStableIdAllocator(),
                new PresenterEntityRuntime(world),
                definitions,
                new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 4),
                new WorldSizeSpec(new WorldAabbCm(-10_000, -10_000, 20_000, 20_000), 100),
                meshes);

            var map = new MapConfig { Id = "manifest.map" };
            map.Entities.Add(new EntitySpawnData { Template = "manifest.root" });

            MapPresentationAssetManifest manifest = loader.BuildPresentationAssetManifest(map);

            Assert.That(manifest.IsSealed, Is.True);
            Assert.That(manifest.Count, Is.EqualTo(4));
            Assert.That(CollectAssetIds(manifest), Is.EquivalentTo(new[]
            {
                rootMeshId,
                swapMeshId,
                planChildMeshId,
                templateChildMeshId,
            }));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static BehaviorSlot AssetBehavior(
        int slotIndex,
        AssetKind assetKind,
        int assetId,
        VisualRenderPath renderPath,
        AssetSwapEntry[]? swaps = null)
    {
        return new BehaviorSlot
        {
            SlotIndex = slotIndex,
            Kind = BehaviorKind.AssetBinding,
            ActiveByDefault = true,
            AssetBinding = new AssetBindingConfig
            {
                AssetKind = assetKind,
                AssetId = assetId,
                RenderPath = renderPath,
                AssetSwapTable = swaps ?? Array.Empty<AssetSwapEntry>(),
            },
        };
    }

    private static PresenterRule CreateOnSpawn(int templateKey, int definitionId)
    {
        return new PresenterRule
        {
            Event = new EventFilter
            {
                Kind = PresentationEventKind.EntitySpawned,
                KeyId = templateKey,
            },
            Command = new PresenterCommand
            {
                CommandKind = PresenterCommandKind.CreatePresenter,
                PresenterDefinitionId = definitionId,
                ScopeSource = PresenterCommandScopeSource.EventPayloadA,
                AnchorKind = PresentationAnchorKind.Entity,
            },
        };
    }

    private static List<int> CollectAssetIds(MapPresentationAssetManifest manifest)
    {
        var ids = new List<int>(manifest.Count);
        for (int i = 0; i < manifest.Count; i++)
        {
            ids.Add(manifest[i].AssetId);
        }

        return ids;
    }
}
