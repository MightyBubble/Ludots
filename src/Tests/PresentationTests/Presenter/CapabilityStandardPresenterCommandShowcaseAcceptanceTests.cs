using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Arch.Core;
using CapabilityStandardPresenterCommandShowcaseMod;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using Ludots.UI.Input;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using Ludots.UI.Surface;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    [NonParallelizable]
    public sealed class CapabilityStandardPresenterCommandShowcaseAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private static readonly string[] BaseMods = { "LudotsCoreMod", "CoreInputMod", "CameraProfilesMod" };
        private const string ModId = "CapabilityStandardPresenterCommandShowcaseMod";
        private const string MapId = "capability_standard_presenter_command_showcase";
        private const string BindingName = "capability_standard_presenter_command_showcase";
        private const string PresetId = "capability_standard_presenter_command_showcase_raylib";

        [Test]
        public void RootAssets_PanelAndAllStationButtons_Mount()
        {
            string repoRoot = FindRepoRoot();
            AssertRootShowcaseAssets(repoRoot);

            using GameEngine engine = CreateEngine(repoRoot, ModId);
            engine.LoadEntryMap(engine.MergedConfig.StartupMapId);
            UIRoot uiRoot = InstallUiHost(engine);
            TickFrames(engine, 2);

            AssertPanelAndButton(uiRoot, "capability-standard-presenter-command-panel", "pcmd-btn-hit");
            string[] allButtons =
            {
                "pcmd-btn-hit", "pcmd-btn-suppress", "pcmd-btn-color", "pcmd-btn-scale",
                "pcmd-btn-refresh", "pcmd-btn-boiler", "pcmd-btn-summon", "pcmd-btn-remove",
                "pcmd-btn-clear", "pcmd-btn-vanish", "pcmd-btn-portal",
            };
            UiScene scene = RequireScene(uiRoot);
            scene.Layout(uiRoot.Width, uiRoot.Height);
            foreach (string buttonId in allButtons)
            {
                Assert.That(scene.FindByElementId(buttonId), Is.Not.Null, buttonId);
            }

            var definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)!;
            string[] expectedDefinitions =
            {
                CapabilityStandardPresenterCommandShowcaseModEntry.FlashUnitDefinitionKey,
                CapabilityStandardPresenterCommandShowcaseModEntry.LampPostDefinitionKey,
                CapabilityStandardPresenterCommandShowcaseModEntry.BoilerDefinitionKey,
                CapabilityStandardPresenterCommandShowcaseModEntry.ChimneySmokeDefinitionKey,
                CapabilityStandardPresenterCommandShowcaseModEntry.PortalDefinitionKey,
                CapabilityStandardPresenterCommandShowcaseModEntry.PortalTargetDefinitionKey,
                CapabilityStandardPresenterCommandShowcaseModEntry.FieldDirectorDefinitionKey,
            };
            foreach (string key in expectedDefinitions)
            {
                Assert.That(definitions.GetId(key), Is.GreaterThan(0), key);
            }

            Assert.That(CountActiveByDefinition(engine, CapabilityStandardPresenterCommandShowcaseModEntry.FlashUnitDefinitionKey), Is.EqualTo(5));
            Assert.That(CountActiveByDefinition(engine, CapabilityStandardPresenterCommandShowcaseModEntry.LampPostDefinitionKey), Is.EqualTo(4));
            Assert.That(CountActiveByDefinition(engine, CapabilityStandardPresenterCommandShowcaseModEntry.ChimneySmokeDefinitionKey), Is.EqualTo(1));
            Assert.That(CountActiveByDefinition(engine, CapabilityStandardPresenterCommandShowcaseModEntry.PortalDefinitionKey), Is.EqualTo(1));
            FindPresenterByOwnerStableId(
                engine,
                CapabilityStandardPresenterCommandShowcaseModEntry.BoilerDefinitionKey,
                CapabilityStandardPresenterCommandShowcaseModEntry.BoilerOwnerStableId);
        }

        [Test]
        public void StationA_HitFlash_SetsTimerEmitsYellowAndRestoresOnExpiry()
        {
            string repoRoot = FindRepoRoot();
            using GameEngine engine = CreateEngine(repoRoot, ModId);
            engine.LoadEntryMap(engine.MergedConfig.StartupMapId);
            UIRoot uiRoot = InstallUiHost(engine);
            TickFrames(engine, 2);

            Entity flashUnit = FindPresenterByOwnerStableId(
                engine,
                CapabilityStandardPresenterCommandShowcaseModEntry.FlashUnitDefinitionKey,
                CapabilityStandardPresenterCommandShowcaseModEntry.FlashUnit0OwnerStableId);
            int colorKey = PresenterParamKeyRegistry.Register("pcmd.unit.color");
            Assert.That(TryGetVectorParam(engine, flashUnit, colorKey, out Vector4 baseColor), Is.True);
            Assert.That(baseColor, Is.EqualTo(new Vector4(0.3f, 0.55f, 1.0f, 1.0f)));

            ClickElement(uiRoot, "pcmd-btn-hit");
            TickFrames(engine, 1);

            PresenterTimerTable timers = GetTimerTable(engine);
            Assert.That(timers.Count, Is.EqualTo(1), "TimerSet 后表内应有 pcmd.flash timer");
            Assert.That(TryGetVectorParam(engine, flashUnit, colorKey, out Vector4 hitColor), Is.True);
            Assert.That(hitColor, Is.EqualTo(new Vector4(1.0f, 0.85f, 0.2f, 1.0f)));

            int proxyStableId = ComposeProxyStableId(engine, flashUnit, CapabilityStandardPresenterCommandShowcaseModEntry.FlashUnitDefinitionKey);
            Vector4 emitColor = FindProxyColor(engine, proxyStableId);
            Assert.That(emitColor, Is.EqualTo(new Vector4(1.0f, 0.85f, 0.2f, 1.0f)), "SetParam vec4 后 emit 请求应携带命中黄色");

            TickSeconds(engine, 0.7f);
            Assert.That(timers.Count, Is.EqualTo(0), "到期后 timer 出表");
            Assert.That(TryGetVectorParam(engine, flashUnit, colorKey, out Vector4 restored), Is.True);
            Assert.That(restored, Is.EqualTo(new Vector4(0.3f, 0.55f, 1.0f, 1.0f)), "TimerExpired 规则应把颜色写回基础蓝");
        }

        [Test]
        public void StationA_Suppress_KillsWildcardTimers_NoExpiryRestore()
        {
            string repoRoot = FindRepoRoot();
            using GameEngine engine = CreateEngine(repoRoot, ModId);
            engine.LoadEntryMap(engine.MergedConfig.StartupMapId);
            UIRoot uiRoot = InstallUiHost(engine);
            TickFrames(engine, 2);

            Entity flashUnit = FindPresenterByOwnerStableId(
                engine,
                CapabilityStandardPresenterCommandShowcaseModEntry.FlashUnitDefinitionKey,
                CapabilityStandardPresenterCommandShowcaseModEntry.FlashUnit0OwnerStableId);
            int colorKey = PresenterParamKeyRegistry.Register("pcmd.unit.color");

            ClickElement(uiRoot, "pcmd-btn-hit");
            TickFrames(engine, 1);
            PresenterTimerTable timers = GetTimerTable(engine);
            Assert.That(timers.Count, Is.EqualTo(1));

            ClickElement(uiRoot, "pcmd-btn-suppress");
            TickFrames(engine, 1);
            Assert.That(timers.Count, Is.EqualTo(0), "TimerKill \"*\" 应立即清空实例 timer");

            TickSeconds(engine, 0.8f);
            Assert.That(TryGetVectorParam(engine, flashUnit, colorKey, out Vector4 suppressed), Is.True);
            Assert.That(suppressed, Is.EqualTo(new Vector4(0.35f, 0.9f, 0.45f, 1.0f)),
                "压制绿应保持：TimerExpired 未发生，没有蓝色复原");
        }

        [Test]
        public void StationB_LampCycle_ParamsSinkIntoAllThreeLamps()
        {
            string repoRoot = FindRepoRoot();
            using GameEngine engine = CreateEngine(repoRoot, ModId);
            engine.LoadEntryMap(engine.MergedConfig.StartupMapId);
            UIRoot uiRoot = InstallUiHost(engine);
            TickFrames(engine, 2);

            int colorKey = PresenterParamKeyRegistry.Register("pcmd.lamp.color");
            int scaleKey = PresenterParamKeyRegistry.Register("pcmd.lamp.scale");
            int[] lampStableIds =
            {
                CapabilityStandardPresenterCommandShowcaseModEntry.Lamp0OwnerStableId,
                CapabilityStandardPresenterCommandShowcaseModEntry.Lamp1OwnerStableId,
                CapabilityStandardPresenterCommandShowcaseModEntry.Lamp2OwnerStableId,
            };

            ClickElement(uiRoot, "pcmd-btn-color");
            TickFrames(engine, 1);
            foreach (int stableId in lampStableIds)
            {
                Entity lamp = FindPresenterByOwnerStableId(
                    engine, CapabilityStandardPresenterCommandShowcaseModEntry.LampPostDefinitionKey, stableId);
                Assert.That(TryGetVectorParam(engine, lamp, colorKey, out Vector4 amber), Is.True);
                Assert.That(amber, Is.EqualTo(new Vector4(1.0f, 0.76f, 0.28f, 1.0f)));
            }

            ClickElement(uiRoot, "pcmd-btn-color");
            TickFrames(engine, 1);
            Entity lamp0 = FindPresenterByOwnerStableId(
                engine, CapabilityStandardPresenterCommandShowcaseModEntry.LampPostDefinitionKey, lampStableIds[0]);
            Assert.That(TryGetVectorParam(engine, lamp0, colorKey, out Vector4 cyan), Is.True);
            Assert.That(cyan, Is.EqualTo(new Vector4(0.35f, 0.9f, 1.0f, 1.0f)));

            ClickElement(uiRoot, "pcmd-btn-scale");
            TickFrames(engine, 1);
            Assert.That(TryGetFloatParam(engine, lamp0, scaleKey, out float scale), Is.True);
            Assert.That(scale, Is.EqualTo(0.8f));

            Entity refreshPillar = FindPresenterByOwnerStableId(
                engine,
                CapabilityStandardPresenterCommandShowcaseModEntry.LampPostDefinitionKey,
                CapabilityStandardPresenterCommandShowcaseModEntry.RefreshPillarOwnerStableId);
            Assert.That(TryGetVectorParam(engine, refreshPillar, colorKey, out Vector4 pillarColor), Is.True);
            Assert.That(pillarColor, Is.EqualTo(new Vector4(1.0f, 0.76f, 0.28f, 1.0f)),
                "对照柱不参与循环 SetParam，保持默认色");
        }

        [Test]
        public void StationB_SinkParamToAsset_ForcesReEmitOnRefreshPillar()
        {
            string repoRoot = FindRepoRoot();
            using GameEngine engine = CreateEngine(repoRoot, ModId);
            engine.LoadEntryMap(engine.MergedConfig.StartupMapId);
            UIRoot uiRoot = InstallUiHost(engine);
            TickFrames(engine, 2);

            Entity refreshPillar = FindPresenterByOwnerStableId(
                engine,
                CapabilityStandardPresenterCommandShowcaseModEntry.LampPostDefinitionKey,
                CapabilityStandardPresenterCommandShowcaseModEntry.RefreshPillarOwnerStableId);
            Assert.That(GetStaticDirty(engine, refreshPillar), Is.EqualTo(0), "settle 后对照柱不应带 dirty");

            using var recording = new RecordingLogBackend();
            Log.Initialize(recording);
            ClickElement(uiRoot, "pcmd-btn-refresh");
            TickFrames(engine, 1);

            Assert.That(
                recording.Infos.Any(m => m.Contains("SinkParamToAsset accepted") && m.Contains("pcmd.lamp.color")),
                Is.True,
                "SinkParamToAsset 应在指令执行内同步完成槽位资产写入并留下 accepted 审计日志（上游 #1091 语义）");
            Assert.That(
                recording.Warnings.Any(m => m.Contains("SinkParamToAsset rejected")),
                Is.False,
                "对照柱 paramDefaults 在 Vector lane 上有当前值，不应被拒绝");

            Assert.That(TryGetVectorParam(engine, refreshPillar, colorKey, out Vector4 pillarColor), Is.True);
            Assert.That(pillarColor, Is.EqualTo(new Vector4(1.0f, 0.76f, 0.28f, 1.0f)),
                "同步写入读取的是黑板当前值，对照柱保持默认色");
        }

        private sealed class RecordingLogBackend : ILogBackend, IDisposable
        {
            public readonly List<string> Infos = new();
            public readonly List<string> Warnings = new();

            public void Write(LogLevel level, in LogChannel channel, string message)
            {
                if (level == LogLevel.Warning)
                {
                    Warnings.Add(message);
                }
                else if (level == LogLevel.Info)
                {
                    Infos.Add(message);
                }
            }

            public void Flush() { }

            public void Dispose() { }
        }

        [Test]
        public void StationC_BoilerSwitch_TogglesChimneyBehaviorMask()
        {
            string repoRoot = FindRepoRoot();
            using GameEngine engine = CreateEngine(repoRoot, ModId);
            engine.LoadEntryMap(engine.MergedConfig.StartupMapId);
            UIRoot uiRoot = InstallUiHost(engine);
            TickFrames(engine, 2);

            Entity chimney = FindPresenterByOwnerStableId(
                engine,
                CapabilityStandardPresenterCommandShowcaseModEntry.ChimneySmokeDefinitionKey,
                CapabilityStandardPresenterCommandShowcaseModEntry.BoilerOwnerStableId);
            Assert.That(GetBehaviorMask(engine, chimney), Is.EqualTo(0u), "activeByDefault:false 时 body slot 不置位");

            ClickElement(uiRoot, "pcmd-btn-boiler");
            TickFrames(engine, 1);
            Assert.That(GetBehaviorMask(engine, chimney), Is.EqualTo(1u), "ActivateBehavior 应置 body slot 位");

            ClickElement(uiRoot, "pcmd-btn-boiler");
            TickFrames(engine, 1);
            Assert.That(GetBehaviorMask(engine, chimney), Is.EqualTo(0u), "DeactivateBehavior 应清 body slot 位");
        }

        [Test]
        public void StationD_SummonScopedRemoveVanishClearAndPortal()
        {
            string repoRoot = FindRepoRoot();
            using GameEngine engine = CreateEngine(repoRoot, ModId);
            engine.LoadEntryMap(engine.MergedConfig.StartupMapId);
            UIRoot uiRoot = InstallUiHost(engine);
            TickFrames(engine, 2);

            ClickElement(uiRoot, "pcmd-btn-summon");
            TickFrames(engine, 1);
            ClickElement(uiRoot, "pcmd-btn-summon");
            TickFrames(engine, 1);
            Assert.That(CountActiveByDefinition(engine, CapabilityStandardPresenterCommandShowcaseModEntry.PortalTargetDefinitionKey), Is.EqualTo(2));

            ClickElement(uiRoot, "pcmd-btn-remove");
            TickFrames(engine, 1);
            Assert.That(CountActiveByDefinition(engine, CapabilityStandardPresenterCommandShowcaseModEntry.PortalTargetDefinitionKey), Is.EqualTo(1),
                "DestroyScopedPresenter 应按 definition+owner+scope 精确拆除一个");

            ClickElement(uiRoot, "pcmd-btn-vanish");
            TickFrames(engine, 1);
            Assert.That(CountActiveByDefinition(engine, CapabilityStandardPresenterCommandShowcaseModEntry.PortalTargetDefinitionKey), Is.EqualTo(0),
                "DestroyPresenter 应按 ExistingInstances 路由销毁单体");

            ClickElement(uiRoot, "pcmd-btn-summon");
            TickFrames(engine, 1);
            ClickElement(uiRoot, "pcmd-btn-summon");
            TickFrames(engine, 1);
            ClickElement(uiRoot, "pcmd-btn-clear");
            TickFrames(engine, 1);
            Assert.That(CountActiveByDefinition(engine, CapabilityStandardPresenterCommandShowcaseModEntry.PortalTargetDefinitionKey), Is.EqualTo(0),
                "DestroyPresenterScope 整域清场后按 definition 查实例应为空");

            Entity portal = FindPresenterByOwnerStableId(
                engine,
                CapabilityStandardPresenterCommandShowcaseModEntry.PortalDefinitionKey,
                CapabilityStandardPresenterCommandShowcaseModEntry.PortalOwnerStableId);
            Vector3 before = engine.World.Get<PresenterWorldPosition>(portal).Value;
            Assert.That(before, Is.EqualTo(new Vector3(0f, 1.4f, -4f)), "初始传送门位置 = owner 变换 + anchor 偏移");

            ClickElement(uiRoot, "pcmd-btn-portal");
            TickFrames(engine, 1);
            Vector3 after = engine.World.Get<PresenterWorldPosition>(portal).Value;
            Assert.That(after, Is.EqualTo(new Vector3(3f, 1.4f, -4f)),
                "InitializeTransform 后世界位置应等于新 owner 变换加 anchor 偏移");
        }

        // ── 夹具 ──

        private static GameEngine CreateEngine(string repoRoot, params string[] showcaseMods)
        {
            var mods = new List<string>(BaseMods);
            mods.AddRange(showcaseMods);
            string assetsRoot = Path.Combine(repoRoot, "assets");
            List<string> modPaths = RepoModPaths.ResolveExplicit(repoRoot, mods);

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallInput(engine);
            engine.Start();
            return engine;
        }

        private static void InstallInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.InputBackend, new NullInputBackend());
            engine.SetService(CoreServiceKeys.UiCaptured, false);
        }

        private static UIRoot InstallUiHost(GameEngine engine)
        {
            var uiRoot = new UIRoot(new SkiaUiRenderer());
            uiRoot.Resize(1920f, 1080f);
            var textMeasurer = new SkiaTextMeasurer();
            var imageSizeProvider = new SkiaImageSizeProvider();
            var surfaceHost = new UiSurfaceHost(uiRoot, textMeasurer, imageSizeProvider);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
            engine.SetService(CoreServiceKeys.UiTextMeasurer, textMeasurer);
            engine.SetService(CoreServiceKeys.UiImageSizeProvider, imageSizeProvider);
            engine.SetService(CoreServiceKeys.UiSurfaceHost, surfaceHost);
            Ludots.UI.Panels.PanelPresentationInstaller.Install(engine);
            return uiRoot;
        }

        private static void TickFrames(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(DeltaTime);
            }
        }

        private static void TickSeconds(GameEngine engine, float seconds)
        {
            int frames = Math.Max(1, (int)MathF.Ceiling(seconds / DeltaTime));
            TickFrames(engine, frames);
        }

        private static string FindRepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(dir))
            {
                if (File.Exists(Path.Combine(dir, "src", "Core", "Ludots.Core.csproj")))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir)!;
            }

            throw new InvalidOperationException("Could not locate repo root.");
        }

        private static Entity FindOwnerByStableId(GameEngine engine, int stableId)
        {
            Entity found = Entity.Null;
            var query = new QueryDescription().WithAll<PresentationStableId>();
            engine.World.Query(in query, (Entity entity, ref PresentationStableId id) =>
            {
                if (id.Value == stableId)
                {
                    found = entity;
                }
            });

            Assert.That(found, Is.Not.EqualTo(Entity.Null), $"owner stableId={stableId} 应存在");
            return found;
        }

        private static Entity FindPresenterByOwnerStableId(GameEngine engine, string definitionKey, int ownerStableId)
        {
            var definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)!;
            int defId = definitions.GetId(definitionKey);
            Assert.That(defId, Is.GreaterThan(0), definitionKey);
            Entity owner = FindOwnerByStableId(engine, ownerStableId);

            Entity found = Entity.Null;
            var query = new QueryDescription().WithAll<PresenterState>();
            engine.World.Query(in query, (Entity entity, ref PresenterState state) =>
            {
                if (state.DefId == defId && state.OwnerEntity == owner)
                {
                    found = entity;
                }
            });

            Assert.That(found, Is.Not.EqualTo(Entity.Null), $"presenter '{definitionKey}' owned by stableId={ownerStableId} 应存在");
            return found;
        }

        private static int CountActiveByDefinition(GameEngine engine, string definitionKey)
        {
            var definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)!;
            var runtime = engine.GetService(CoreServiceKeys.PresenterEntityRuntime)!;
            return runtime.GetActiveByDefinition(definitions.GetId(definitionKey)).Count;
        }

        private static bool TryGetVectorParam(GameEngine engine, Entity presenter, int key, out Vector4 value)
        {
            return ResolveRuntime(engine).TryResolveVector(presenter, key, out value);
        }

        private static bool TryGetFloatParam(GameEngine engine, Entity presenter, int key, out float value)
        {
            return ResolveRuntime(engine).TryResolveFloat(presenter, key, out value);
        }

        private static Ludots.Core.Presentation.Presenters.PresenterEntityRuntime ResolveRuntime(GameEngine engine)
        {
            return engine.GetService(CoreServiceKeys.PresenterEntityRuntime)
                ?? throw new InvalidOperationException("PresenterEntityRuntime missing.");
        }

        private static byte GetStaticDirty(GameEngine engine, Entity presenter)
        {
            Assert.That(engine.World.Has<PresenterEmitCache>(presenter), Is.True);
            return engine.World.Get<PresenterEmitCache>(presenter).StaticDirty;
        }

        private static uint GetBehaviorMask(GameEngine engine, Entity presenter)
        {
            return engine.World.Get<PresenterState>(presenter).BehaviorActiveMask;
        }

        private static PresenterTimerTable GetTimerTable(GameEngine engine)
        {
            PresenterTimerSystem timerSystem = FindPresentationSystem<PresenterTimerSystem>(engine);
            FieldInfo field = typeof(PresenterTimerSystem).GetField("_timers", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("PresenterTimerSystem._timers field missing.");
            return (PresenterTimerTable)(field.GetValue(timerSystem) ?? throw new InvalidOperationException("PresenterTimerTable missing."));
        }

        private static int ComposeProxyStableId(GameEngine engine, Entity presenter, string definitionKey)
        {
            var definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)!;
            int defId = definitions.GetId(definitionKey);
            int presenterStableId = engine.World.Get<PresenterState>(presenter).StableId;
            return PresenterBehaviorRuntimeUtility.ComposeVisualStableId(presenterStableId, 0, AssetKind.Mesh, defId);
        }

        private static Vector4 FindProxyColor(GameEngine engine, int proxyStableId)
        {
            var proxyBuffer = engine.GetService(CoreServiceKeys.PresentationVisualProxyBuffer)
                ?? throw new InvalidOperationException("PresentationVisualProxyBuffer missing.");
            ReadOnlySpan<Ludots.Core.Presentation.Rendering.PresentationVisualProxy> span = proxyBuffer.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i].StableId == proxyStableId)
                {
                    return span[i].Color;
                }
            }

            Assert.Fail($"visual proxy stableId={proxyStableId} not emitted this frame.");
            return default;
        }

        private static void DriveRulesAndRuntime(GameEngine engine, int refreshPillarOwnerStableId)
        {
            PresenterRuleSystem rules = FindPresentationSystem<PresenterRuleSystem>(engine);
            PresenterRuntimeSystem runtime = FindPresentationSystem<PresenterRuntimeSystem>(engine);
            var events = engine.GetService(CoreServiceKeys.PresentationEventStream)
                ?? throw new InvalidOperationException("PresentationEventStream missing.");
            Entity owner = FindOwnerByStableId(engine, refreshPillarOwnerStableId);

            Assert.That(events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.GameplayEvent,
                KeyId = TagRegistry.Register("pcmd.lamp.refresh"),
                Source = owner,
                Target = owner,
            }), Is.True);

            ((Arch.System.ISystem<float>)rules).Update(DeltaTime);
            ((Arch.System.ISystem<float>)runtime).Update(DeltaTime);
        }

        private static void DriveEmitAndFlush(GameEngine engine)
        {
            PresenterEmitSystem emit = FindPresentationSystem<PresenterEmitSystem>(engine);
            PresentationRequestFlushSystem flush = FindPresentationSystem<PresentationRequestFlushSystem>(engine);
            ((Arch.System.ISystem<float>)emit).Update(DeltaTime);
            ((Arch.System.ISystem<float>)flush).Update(DeltaTime);
        }

        private static T FindPresentationSystem<T>(GameEngine engine)
            where T : class
        {
            FieldInfo field = typeof(GameEngine).GetField("_presentationSystems", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("GameEngine._presentationSystems field missing.");
            var systems = field.GetValue(engine) as List<Arch.System.ISystem<float>>
                ?? throw new InvalidOperationException("GameEngine presentation systems missing.");
            foreach (Arch.System.ISystem<float> system in systems)
            {
                if (system is T typed)
                {
                    return typed;
                }
            }

            throw new InvalidOperationException($"Presentation system '{typeof(T).Name}' not registered.");
        }

        private static UiScene RequireScene(UIRoot root)
        {
            return root.Scene ?? throw new InvalidOperationException("UI scene is not mounted.");
        }

        private static void AssertPanelAndButton(UIRoot root, string panelElementId, string buttonElementId)
        {
            UiScene scene = RequireScene(root);
            scene.Layout(root.Width, root.Height);
            Assert.That(scene.FindByElementId(panelElementId), Is.Not.Null);
            Assert.That(scene.FindByElementId(buttonElementId), Is.Not.Null);
        }

        private static void ClickElement(UIRoot root, string elementId)
        {
            UiScene scene = RequireScene(root);
            scene.Layout(root.Width, root.Height);
            UiNode node = scene.FindByElementId(elementId)
                ?? throw new InvalidOperationException($"UI element '{elementId}' was not found.");
            Assert.That(node.ActionHandles.Count, Is.GreaterThan(0), $"UI element '{elementId}' must be clickable.");

            float x = node.LayoutRect.X + (node.LayoutRect.Width * 0.5f);
            float y = node.LayoutRect.Y + (node.LayoutRect.Height * 0.5f);
            UiNode? hitNode = scene.HitTest(x, y);
            Assert.That(
                hitNode?.ElementId,
                Is.EqualTo(elementId),
                $"Pointer click for '{elementId}' hit '{hitNode?.ElementId ?? hitNode?.TagName ?? "<none>"}' instead.");

            bool downHandled = root.HandleInput(new PointerEvent
            {
                PointerId = 0,
                Action = PointerAction.Down,
                Button = PointerButton.Left,
                X = x,
                Y = y,
            });
            bool upHandled = root.HandleInput(new PointerEvent
            {
                PointerId = 0,
                Action = PointerAction.Up,
                Button = PointerButton.Left,
                X = x,
                Y = y,
            });

            Assert.That(downHandled || upHandled, Is.True, $"UI element '{elementId}' did not handle pointer click.");
        }

        private static void AssertRootShowcaseAssets(string repoRoot)
        {
            string modDir = Path.Combine(repoRoot, "mods", "showcases", "capability_standard", ModId);
            AssertLauncherBinding(repoRoot, BindingName, ModId);
            AssertLauncherPreset(repoRoot, PresetId, BindingName);
            Assert.That(File.Exists(Path.Combine(modDir, "mod.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(modDir, $"{ModId}.csproj")), Is.True);
            Assert.That(File.Exists(Path.Combine(modDir, "assets", "game.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(modDir, "assets", "Maps", $"{MapId}.json")), Is.True);
            string[] presenterShards =
            {
                "capability_standard.presenter_command.ground.json",
                "capability_standard.presenter_command.flash_plaza.json",
                "capability_standard.presenter_command.lamp_params.json",
                "capability_standard.presenter_command.boiler_switch.json",
                "capability_standard.presenter_command.portal_field.json",
            };
            foreach (string shard in presenterShards)
            {
                Assert.That(File.Exists(Path.Combine(modDir, "assets", "Presentation", "presenters", shard)), Is.True, shard);
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(modDir, "assets", "game.json")));
            Assert.That(document.RootElement.GetProperty("startupMapId").GetString(), Is.EqualTo(MapId));
        }

        private static void AssertLauncherBinding(string repoRoot, string bindingName, string modId)
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "launcher.config.json")));
            foreach (JsonElement binding in document.RootElement.GetProperty("bindings").EnumerateArray())
            {
                if (!string.Equals(binding.GetProperty("name").GetString(), bindingName, StringComparison.Ordinal))
                {
                    continue;
                }

                JsonElement target = binding.GetProperty("target");
                Assert.That(target.GetProperty("type").GetString(), Is.EqualTo("path"));
                Assert.That(
                    target.GetProperty("value").GetString(),
                    Is.EqualTo($"mods/showcases/capability_standard/{modId}"));
                Assert.That(target.GetProperty("projectPath").GetString(), Is.EqualTo($"{modId}.csproj"));
                return;
            }

            Assert.Fail($"Launcher binding '{bindingName}' is missing.");
        }

        private static void AssertLauncherPreset(string repoRoot, string presetId, string bindingName)
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "launcher.presets.json")));
            foreach (JsonElement preset in document.RootElement.GetProperty("presets").EnumerateArray())
            {
                if (!string.Equals(preset.GetProperty("id").GetString(), presetId, StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.That(preset.GetProperty("adapterId").GetString(), Is.EqualTo("raylib"));
                JsonElement selectors = preset.GetProperty("selectors");
                Assert.That(selectors.GetArrayLength(), Is.EqualTo(1));
                Assert.That(selectors[0].GetString(), Is.EqualTo($"${bindingName}"));
                return;
            }

            Assert.Fail($"Launcher preset '{presetId}' is missing.");
        }

        private sealed class NullInputBackend : IInputBackend
        {
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => false;
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }
    }
}
