using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PresenterSinkParamToAssetTests
    {
        private string _root = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "Ludots_PresenterSinkParamToAssetTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        // ── 配置加载 ──

        [Test]
        public void Load_SinkParamToAssetWithSelectorPayload_ParsesKeyLaneAndSingleRuntimeRoute()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "sink.lamp",
                    "behaviors": [
                      {
                        "slot": "body",
                        "kind": "AssetBinding",
                        "activeByDefault": true,
                        "assetBinding": {
                          "assetKind": "Mesh",
                          "assetId": "ref_mesh_cube",
                          "renderPath": "StaticMesh",
                          "mobility": "Static",
                          "scaleParamKey": "sink.fixture.scale"
                        }
                      }
                    ]
                  },
                  {
                    "id": "sink.actor",
                    "rules": [
                      {
                        "event": { "kind": "GameplayEvent", "keyId": "Event.Sink" },
                        "command": {
                          "kind": "SinkParamToAsset",
                          "definitionId": "sink.lamp",
                          "scopeTag": "sinkScope",
                          "paramKey": "sink.fixture.scale",
                          "paramLane": "Float"
                        }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveBehaviorAssetId: (kind, key) =>
                    kind == AssetKind.Mesh && string.Equals(key, "ref_mesh_cube", StringComparison.Ordinal) ? 42 : 0);
            loader.Load(catalog);

            Assert.That(registry.TryGet(registry.GetId("sink.actor"), out var definition), Is.True);
            var command = definition.Rules[0].Command;
            Assert.That(command.CommandKind, Is.EqualTo(PresenterCommandKind.SinkParamToAsset));
            Assert.That(command.RouteStrategy, Is.EqualTo(PerformerCommandRouteStrategy.SingleRuntime));
            Assert.That(command.HasParamPayload, Is.True);
            Assert.That(command.ParamKey, Is.EqualTo(PresenterParamKeyRegistry.Register("sink.fixture.scale")));
            Assert.That(command.ParamLane, Is.EqualTo(ParamLane.Float));
            Assert.That(command.PresenterDefinitionId, Is.EqualTo(registry.GetId("sink.lamp")));
            Assert.That(command.ScopeTag, Is.EqualTo(PresenterScopeTagRegistry.GetId("sinkScope")));
        }

        [Test]
        public void Load_SinkParamToAssetWithoutPayload_MeansRefreshAllLanes()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "sink.lamp",
                    "rules": [
                      {
                        "event": { "kind": "GameplayEvent", "keyId": "Event.Sink" },
                        "command": {
                          "kind": "SinkParamToAsset",
                          "definitionId": "sink.lamp",
                          "scopeTag": "sinkScope"
                        }
                      }
                    ]
                  }
                ]
                """);

            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(pipeline, registry);
            loader.Load(catalog);

            Assert.That(registry.TryGet(registry.GetId("sink.lamp"), out var definition), Is.True);
            var command = definition.Rules[0].Command;
            Assert.That(command.CommandKind, Is.EqualTo(PresenterCommandKind.SinkParamToAsset));
            Assert.That(command.RouteStrategy, Is.EqualTo(PerformerCommandRouteStrategy.SingleRuntime));
            Assert.That(command.HasParamPayload, Is.False);
            Assert.That(command.ParamKey, Is.EqualTo(0), "无 selector payload 时 paramKey 应保持 0（刷新全部 lane）");
        }

        [Test]
        public void Load_SinkParamToAssetWithValueFields_Rejects()
        {
            WriteCatalog();
            InvalidOperationException ex = AssertLoaderThrows(
                """
                [
                  {
                    "id": "sink.actor",
                    "rules": [
                      {
                        "event": { "kind": "GameplayEvent", "keyId": "Event.Sink" },
                        "command": {
                          "kind": "SinkParamToAsset",
                          "paramKey": "sink.fixture.scale",
                          "paramLane": "Float",
                          "valueSource": "Fixed",
                          "paramValue": 1
                        }
                      }
                    ]
                  }
                ]
                """);

            Assert.That(ex!.Message, Does.Contain("SinkParamToAsset accepts only paramKey/paramLane"));
        }

        [Test]
        public void Load_SinkParamToAssetWithPartialSelector_Rejects()
        {
            WriteCatalog();
            InvalidOperationException ex = AssertLoaderThrows(
                """
                [
                  {
                    "id": "sink.actor",
                    "rules": [
                      {
                        "event": { "kind": "GameplayEvent", "keyId": "Event.Sink" },
                        "command": {
                          "kind": "SinkParamToAsset",
                          "paramKey": "sink.fixture.scale"
                        }
                      }
                    ]
                  }
                ]
                """);

            Assert.That(ex!.Message, Does.Contain("paramLane"));
        }

        // ── 运行时：强制重 emit ──

        [Test]
        public void Runtime_SinkParamToAsset_ValueUnchanged_MarksStaticDirtyAndReEmits()
        {
            using var fixture = SinkFixture.Create(out int scaleParamKey);
            Entity presenter = fixture.CreateLampPresenter(scaleParamKey, scopeTag: 500);
            fixture.Instances.SetParam(presenter, scaleParamKey, ParamLane.Float, 2.5f, 0, Vector4.Zero);

            fixture.Emit.Update(0.016f);
            Assert.That(fixture.Requests.Count, Is.EqualTo(1), "首次 emit 应产出一个 VisualProxy 请求");
            Assert.That(fixture.Requests.GetSpan()[0].VisualProxy.Scale.X, Is.EqualTo(2.5f), "scale param 应作为倍率进入 emit");
            fixture.Requests.Clear();
            Assert.That(fixture.World.Get<PresenterEmitCache>(presenter).StaticDirty, Is.EqualTo(0), "emit 后 dirty 应被清除");

            fixture.Emit.Update(0.016f);
            Assert.That(fixture.Requests.Count, Is.EqualTo(0), "值未变且未强制刷新时，静态 presenter 不应重复 emit");

            fixture.Commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.SinkParamToAsset,
                CommandKindId = (byte)PresenterCommandKind.SinkParamToAsset,
                RouteStrategy = PerformerCommandRouteStrategy.SingleRuntime,
                PresenterEntity = presenter,
            });
            fixture.Runtime.Update(0.016f);
            Assert.That(fixture.World.Get<PresenterEmitCache>(presenter).StaticDirty, Is.EqualTo(1),
                "无 selector payload 的 SinkParamToAsset 应无条件把实例标记为需要重 emit");
            fixture.Emit.Update(0.016f);
            Assert.That(fixture.Requests.Count, Is.EqualTo(1), "SinkParamToAsset 后即使值未变也应重 emit");
            Assert.That(fixture.Requests.GetSpan()[0].VisualProxy.Scale.X, Is.EqualTo(2.5f), "重 emit 应 sink 出同样的 param 派生值");
            Assert.That(fixture.World.Get<PresenterEmitCache>(presenter).StaticDirty, Is.EqualTo(0));
        }

        [Test]
        public void Runtime_SinkParamToAsset_UnknownParamKey_DoesNotReEmit()
        {
            using var fixture = SinkFixture.Create(out int scaleParamKey);
            Entity presenter = fixture.CreateLampPresenter(scaleParamKey, scopeTag: 500);
            int unrelatedKey = PresenterParamKeyRegistry.Register("sink.fixture.unrelated");
            fixture.Instances.SetParam(presenter, scaleParamKey, ParamLane.Float, 1.5f, 0, Vector4.Zero);

            fixture.Emit.Update(0.016f);
            fixture.Requests.Clear();
            Assert.That(fixture.World.Get<PresenterEmitCache>(presenter).StaticDirty, Is.EqualTo(0));

            fixture.Commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.SinkParamToAsset,
                CommandKindId = (byte)PresenterCommandKind.SinkParamToAsset,
                RouteStrategy = PerformerCommandRouteStrategy.SingleRuntime,
                PresenterEntity = presenter,
                HasParamPayload = true,
                ParamKey = unrelatedKey,
                ParamLane = ParamLane.Float,
            });
            fixture.Runtime.Update(0.016f);

            Assert.That(fixture.World.Get<PresenterEmitCache>(presenter).StaticDirty, Is.EqualTo(0),
                "指定 paramKey 不在定义影响面内时，强制刷新应保持无操作");
        }

        // ── 规则管线：GameplayEvent → SinkParamToAsset(scoped) ──

        [Test]
        public void Pipeline_GameplayEvent_SinkParamToAssetRule_RefreshesScopedInstance()
        {
            using var fixture = SinkFixture.Create(out int scaleParamKey);
            int eventKeyId = TagRegistry.Register("sink.fixture.refresh");
            int lampDefId = fixture.RegisterLampDefinition(scaleParamKey);
            fixture.RegisterActorWithSinkRule(lampDefId, eventKeyId);
            Entity presenter = fixture.CreatePresenter(lampDefId, PresenterScopeTagRegistry.GetId("sinkScope"));
            fixture.Instances.SetParam(presenter, scaleParamKey, ParamLane.Float, 3.0f, 0, Vector4.Zero);

            fixture.Emit.Update(0.016f);
            fixture.Requests.Clear();

            fixture.Events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.GameplayEvent,
                KeyId = eventKeyId,
                Source = fixture.Owner,
                Target = fixture.Owner,
            });
            fixture.Rules.Update(0.016f);
            Assert.That(fixture.Commands.Count, Is.EqualTo(1), "规则应产出一条 SinkParamToAsset 命令");
            Assert.That(fixture.Commands.GetSpan()[0].CommandKind, Is.EqualTo(PresenterCommandKind.SinkParamToAsset));

            fixture.Runtime.Update(0.016f);
            Assert.That(fixture.World.Get<PresenterEmitCache>(presenter).StaticDirty, Is.EqualTo(1),
                "SingleRuntime 命令应通过 definitionId+scopeTag 解析到 scoped 实例并置重 emit 标记");

            fixture.Emit.Update(0.016f);
            Assert.That(fixture.Requests.Count, Is.EqualTo(1));
            Assert.That(fixture.Requests.GetSpan()[0].VisualProxy.Scale.X, Is.EqualTo(3.0f));
        }

        // ── 夹具 ──

        private (VirtualFileSystem Vfs, ModLoader ModLoader, ConfigPipeline Pipeline, ConfigCatalog Catalog) BuildPipeline()
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(_root, "Core"));
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            return (vfs, modLoader, pipeline, catalog);
        }

        private InvalidOperationException? AssertLoaderThrows(string presentersJson)
        {
            WriteCatalog();
            WritePresenters(presentersJson);
            var (_, _, pipeline, catalog) = BuildPipeline();
            var registry = new PresenterDefinitionRegistry();
            return Assert.Throws<InvalidOperationException>(() =>
                new PresenterDefinitionConfigLoader(pipeline, registry).Load(catalog));
        }

        private void WriteCatalog()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/presenters.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
        }

        private void WritePresenters(string content)
        {
            WriteFile("Core", "Presentation/presenters.json", content);
        }

        private void WriteFile(string modId, string relativePath, string content)
        {
            string dir = Path.Combine(_root, modId, Path.GetDirectoryName(relativePath) ?? string.Empty);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, Path.GetFileName(relativePath)), content);
        }

        private sealed class SinkFixture : IDisposable
        {
            public readonly World World;
            public readonly PresenterCommandBuffer Commands;
            public readonly PresentationEventStream Events;
            public readonly PresenterEntityRuntime Instances;
            public readonly PresenterDefinitionRegistry Definitions;
            public readonly PresenterRuntimeSystem Runtime;
            public readonly PresenterRuleSystem Rules;
            public readonly PresentationRequestBuffer Requests;
            public readonly PresenterEmitSystem Emit;
            public readonly Entity Owner;

            private SinkFixture()
            {
                World = Arch.Core.World.Create();
                Commands = new PresenterCommandBuffer();
                Events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
                Instances = new PresenterEntityRuntime(World);
                Definitions = new PresenterDefinitionRegistry();
                Requests = new PresentationRequestBuffer();
                Owner = World.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
                Runtime = new PresenterRuntimeSystem(
                    World,
                    Commands,
                    Events,
                    new TransientMarkerBuffer(),
                    Requests,
                    Instances,
                    new PresentationStableIdAllocator(),
                    Definitions);
                Rules = new PresenterRuleSystem(
                    World,
                    Events,
                    Commands,
                    Definitions,
                    Instances,
                    new Ludots.Core.GraphRuntime.GraphProgramRegistry(),
                    new Ludots.Core.NodeLibraries.GASGraph.Host.GasGraphRuntimeApi(World, spatialQueries: null, coords: null, eventBus: null),
                    new Dictionary<string, object>());
                Emit = new PresenterEmitSystem(
                    World,
                    Instances,
                    Definitions,
                    Requests,
                    new Dictionary<string, object>());
            }

            public static SinkFixture Create(out int scaleParamKey)
            {
                scaleParamKey = PresenterParamKeyRegistry.Register("sink.fixture.scale");
                return new SinkFixture();
            }

            public Entity CreateLampPresenter(int scaleParamKey, int scopeTag)
            {
                int defId = Definitions.Register("sink.lamp.static", CreateStaticLampDefinition(scaleParamKey));
                return CreatePresenter(defId, scopeTag);
            }

            public Entity CreatePresenter(int defId, int scopeTag)
            {
                PresenterDefinition definition = Definitions.Get(defId);
                return Instances.Create(
                    defId,
                    Owner,
                    scopeTag,
                    PresentationAnchorKind.Entity,
                    Vector3.Zero,
                    stableId: 9001,
                    Entity.Null,
                    definition);
            }

            public int RegisterLampDefinition(int scaleParamKey)
            {
                return Definitions.Register("sink.lamp.scoped", CreateStaticLampDefinition(scaleParamKey));
            }

            public void RegisterActorWithSinkRule(int lampDefId, int eventKeyId)
            {
                Definitions.Register("sink.actor", new PresenterDefinition
                {
                    Rules = new[]
                    {
                        new PresenterRule
                        {
                            Event = new EventFilter
                            {
                                Kind = PresentationEventKind.GameplayEvent,
                                KeyId = eventKeyId,
                            },
                            Command = new PresenterCommand
                            {
                                CommandKind = PresenterCommandKind.SinkParamToAsset,
                                CommandKindId = (byte)PresenterCommandKind.SinkParamToAsset,
                                RouteStrategy = PerformerCommandRouteStrategy.SingleRuntime,
                                PresenterDefinitionId = lampDefId,
                                ScopeTag = PresenterScopeTagRegistry.GetId("sinkScope"),
                                ScopeSource = PresenterCommandScopeSource.Fixed,
                            },
                        },
                    },
                });
            }

            private static PresenterDefinition CreateStaticLampDefinition(int scaleParamKey)
            {
                return new PresenterDefinition
                {
                    Behaviors = new[]
                    {
                        new BehaviorSlot
                        {
                            SlotIndex = 0,
                            Kind = BehaviorKind.AssetBinding,
                            ActiveByDefault = true,
                            AssetBinding = new AssetBindingConfig
                            {
                                AssetKind = AssetKind.Mesh,
                                AssetId = 101,
                                MaterialId = 201,
                                RenderPath = VisualRenderPath.StaticMesh,
                                Mobility = VisualMobility.Static,
                                LocalScale = Vector3.One,
                                ScaleParamKey = scaleParamKey,
                            },
                        },
                    },
                };
            }

            public void Dispose()
            {
                Rules.Dispose();
                Runtime.Dispose();
                Emit.Dispose();
                World.Dispose();
            }
        }
    }
}
