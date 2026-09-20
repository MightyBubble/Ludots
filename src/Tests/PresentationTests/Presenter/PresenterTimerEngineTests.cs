using System;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// Full GameEngine frame-loop E2E for presenter named timers: real mod config load,
    /// engine system registration order (Timer → Rules → Runtime), event stream and
    /// command buffer reached through engine services.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class PresenterTimerEngineTests
    {
        private static readonly string[] TimerMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "PresenterTimerTestMod",
        };

        [Test]
        public void EngineLoop_HitFlash_ExpiryCreatesChildren_TagLostInterruptSuppresses()
        {
            using var engine = CreateEngine();
            engine.LoadStartupMap();
            Tick(engine, 5);

            var commands = engine.GetService(CoreServiceKeys.PresenterCommandBuffer);
            var events = engine.GetService(CoreServiceKeys.PresentationEventStream);
            var definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry);

            int rootDefId = definitions.GetId("pt_e2e.flash_unit");
            int childDefId = definitions.GetId("pt_e2e.phase_child");
            int markerDefId = definitions.GetId("pt_e2e.wildcard_marker");
            Assert.Multiple(() =>
            {
                Assert.That(rootDefId, Is.GreaterThan(0), "mod 配置应加载 flash_unit 定义");
                Assert.That(childDefId, Is.GreaterThan(0));
                Assert.That(markerDefId, Is.GreaterThan(0));
            });

            Entity unitA = engine.World.Create();
            Entity unitB = engine.World.Create();

            EnqueueCreate(commands, rootDefId, unitA, scopeTag: 1001);
            EnqueueCreate(commands, rootDefId, unitB, scopeTag: 1002);
            Tick(engine, 2);
            Assert.That(CountPresenters(engine.World, rootDefId, unitA), Is.EqualTo(1), "A 的 flash presenter 应上线");
            Assert.That(CountPresenters(engine.World, rootDefId, unitB), Is.EqualTo(1), "B 的 flash presenter 应上线");

            int hitFlashId = TagRegistry.GetId("PT.HitFlash");
            int suppressedId = TagRegistry.GetId("PT.Suppressed");
            Assert.That(hitFlashId, Is.GreaterThan(0));
            Assert.That(suppressedId, Is.GreaterThan(0));

            // 两个单位同帧受击 → 各挂 0.3s 命名 timer
            Fire(events, PresentationEventKind.GameplayEvent, hitFlashId, unitA, magnitude: 1f);
            Fire(events, PresentationEventKind.GameplayEvent, hitFlashId, unitB, magnitude: 1f);
            Tick(engine, 2);

            // B 中途被打断：Suppressed tag 丢失 → TimerKill "*" 清掉实例全部 timer
            Fire(events, PresentationEventKind.TagEffectiveChanged, suppressedId, unitB, magnitude: 0f);
            Tick(engine, 2);

            // 跑过 0.3s 到期窗口（1/60 帧率下 18 帧）
            Tick(engine, 40);

            Assert.Multiple(() =>
            {
                Assert.That(CountPresenters(engine.World, childDefId, unitA), Is.EqualTo(1), "A 到期应建出精确匹配的 phase_child");
                Assert.That(CountPresenters(engine.World, markerDefId, unitA), Is.EqualTo(1), "A 到期应同时命中 TimerExpired \"*\" 通配规则");
                Assert.That(CountPresenters(engine.World, childDefId, unitB), Is.EqualTo(0), "B 被打断后不应有 TimerExpired 后续");
                Assert.That(CountPresenters(engine.World, markerDefId, unitB), Is.EqualTo(0), "B 的 timer 已被 TimerKill 清掉，通配规则也不应命中");
                Assert.That(CountPresenters(engine.World, rootDefId, unitA), Is.EqualTo(1), "A 的 root presenter 应存活");
                Assert.That(CountPresenters(engine.World, rootDefId, unitB), Is.EqualTo(1), "打断只清 timer，B 的 root presenter 应存活");
            });
        }

        private static void EnqueueCreate(PresenterCommandBuffer commands, int definitionId, Entity owner, int scopeTag)
        {
            Assert.That(commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.CreatePresenter,
                CommandKindId = (byte)PresenterCommandKind.CreatePresenter,
                RouteStrategy = PresenterCommandRouteStrategy.CreatePresenter,
                PresenterDefinitionId = definitionId,
                ParentEntity = Entity.Null,
                ScopeTag = scopeTag,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = owner,
                Target = owner,
            }), Is.True);
        }

        private static void Fire(PresentationEventStream events, PresentationEventKind kind, int keyId, Entity source, float magnitude)
        {
            Assert.That(events.TryAdd(new PresentationEvent
            {
                Kind = kind,
                KeyId = keyId,
                Source = source,
                Target = source,
                Magnitude = magnitude,
            }), Is.True);
        }

        private static int CountPresenters(World world, int definitionId, Entity owner)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<PresenterState>();
            world.Query(in query, (ref PresenterState state) =>
            {
                if (state.DefId == definitionId && state.OwnerEntity == owner)
                {
                    count++;
                }
            });
            return count;
        }

        private static GameEngine CreateEngine()
        {
            string repoRoot = PresenterBlacksmithShowcaseTestHarness.FindRepoRoot();
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, TimerMods),
                Path.Combine(repoRoot, "assets"));

            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var input = new PlayerInputHandler(new NullInputBackend(), inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                input.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, input);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            HeadlessPresentationTestHost.Install(engine);
            engine.Start();
            return engine;
        }

        private static void Tick(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(1f / 60f);
                HeadlessPresentationTestHost.UpdateCamera(engine);
            }
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
