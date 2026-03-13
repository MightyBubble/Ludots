using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using InteractionShowcaseMod;
using Ludots.Core.Engine;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [NonParallelizable]
    public sealed class C3AnyUnitConditionalTests
    {
        private const float DeltaTime = 1f / 60f;

        [Test]
        public void C3_HostileTarget_OnlyHostileBranchApplies()
        {
            using var scenario = StartScenario();
            C3Snapshot hostileApplied = TickUntil(
                scenario.Engine,
                snapshot => string.Equals(snapshot.Stage, "hostile_polymorph_applied", StringComparison.OrdinalIgnoreCase));

            Assert.That(hostileApplied.CastSubmitted, Is.True);
            Assert.That(hostileApplied.LastAttemptTargetName, Is.EqualTo(InteractionShowcaseIds.C3HostileTargetName));
            Assert.That(hostileApplied.Mana, Is.EqualTo(100f).Within(0.001f));
            Assert.That(hostileApplied.HostileMoveSpeed, Is.EqualTo(80f).Within(0.001f));
            Assert.That(hostileApplied.HostilePolymorphActive, Is.True);
            Assert.That(hostileApplied.HostilePolymorphCount, Is.EqualTo(1));
            Assert.That(hostileApplied.HostilePolymorphApplied, Is.True);
            Assert.That(hostileApplied.FriendlyMoveSpeed, Is.EqualTo(180f).Within(0.001f));
            Assert.That(hostileApplied.FriendlyHasteActive, Is.False);
            Assert.That(hostileApplied.FriendlyHasteCount, Is.EqualTo(0));
            Assert.That(hostileApplied.FriendlyHasteApplied, Is.False);
        }

        [Test]
        public void C3_FriendlyTarget_OnlyFriendlyBranchApplies_AfterHostilePhase()
        {
            using var scenario = StartScenario();
            C3Snapshot friendlyApplied = TickUntil(
                scenario.Engine,
                snapshot => string.Equals(snapshot.Stage, "friendly_haste_applied", StringComparison.OrdinalIgnoreCase));

            Assert.That(friendlyApplied.CastSubmitted, Is.True);
            Assert.That(friendlyApplied.LastAttemptTargetName, Is.EqualTo(InteractionShowcaseIds.C3FriendlyTargetName));
            Assert.That(friendlyApplied.Mana, Is.EqualTo(100f).Within(0.001f));
            Assert.That(friendlyApplied.HostileMoveSpeed, Is.EqualTo(80f).Within(0.001f));
            Assert.That(friendlyApplied.HostilePolymorphActive, Is.True);
            Assert.That(friendlyApplied.HostilePolymorphCount, Is.EqualTo(1));
            Assert.That(friendlyApplied.HostilePolymorphApplied, Is.True);
            Assert.That(friendlyApplied.FriendlyMoveSpeed, Is.EqualTo(260f).Within(0.001f));
            Assert.That(friendlyApplied.FriendlyHasteActive, Is.True);
            Assert.That(friendlyApplied.FriendlyHasteCount, Is.EqualTo(1));
            Assert.That(friendlyApplied.FriendlyHasteApplied, Is.True);
        }

        [Test]
        public void C3_Autoplay_ProgressesThroughExpectedStageOrder()
        {
            using var scenario = StartScenario();

            string[] expectedStages =
            {
                "warmup",
                "hostile_order_submitted",
                "hostile_polymorph_applied",
                "friendly_order_setup",
                "friendly_order_submitted",
                "friendly_haste_applied",
                "complete"
            };

            var firstSeenTicks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int tick = 0; tick <= 480; tick++)
            {
                C3Snapshot snapshot = Sample(scenario.Engine);
                if (!firstSeenTicks.ContainsKey(snapshot.Stage))
                {
                    firstSeenTicks[snapshot.Stage] = snapshot.Tick;
                }

                if (string.Equals(snapshot.Stage, "complete", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                scenario.Engine.Tick(DeltaTime);
            }

            foreach (string stage in expectedStages)
            {
                Assert.That(firstSeenTicks.ContainsKey(stage), Is.True, $"Expected stage '{stage}' to appear.");
            }

            for (int index = 1; index < expectedStages.Length; index++)
            {
                Assert.That(
                    firstSeenTicks[expectedStages[index]],
                    Is.GreaterThanOrEqualTo(firstSeenTicks[expectedStages[index - 1]]),
                    $"Stage '{expectedStages[index]}' should not appear before '{expectedStages[index - 1]}'.");
            }
        }

        private static ScenarioRuntime StartScenario()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var engine = new GameEngine();

            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, new[]
            {
                "LudotsCoreMod",
                "ArpgDemoMod",
                "InteractionShowcaseMod"
            });

            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallDummyInput(engine);
            engine.Start();
            engine.LoadMap(InteractionShowcaseIds.C3AnyUnitConditionalMapId);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0), "Map load should not emit trigger errors.");
            return new ScenarioRuntime(engine);
        }

        private static C3Snapshot TickUntil(GameEngine engine, Func<C3Snapshot, bool> predicate, int maxTicks = 480)
        {
            for (int tick = 0; tick <= maxTicks; tick++)
            {
                C3Snapshot snapshot = Sample(engine);
                if (predicate(snapshot))
                {
                    return snapshot;
                }

                engine.Tick(DeltaTime);
            }

            Assert.Fail($"Scenario did not reach the expected state within {maxTicks} ticks.");
            return default;
        }

        private static C3Snapshot Sample(GameEngine engine)
        {
            return new C3Snapshot(
                Tick: engine.GameSession.CurrentTick,
                Stage: ReadString(engine, InteractionShowcaseRuntimeKeys.Stage, "not_started"),
                Mana: ReadFloat(engine, InteractionShowcaseRuntimeKeys.HeroMana),
                HostileMoveSpeed: ReadFloat(engine, InteractionShowcaseRuntimeKeys.C3HostileTargetMoveSpeed),
                FriendlyMoveSpeed: ReadFloat(engine, InteractionShowcaseRuntimeKeys.C3FriendlyTargetMoveSpeed),
                HostilePolymorphActive: ReadBool(engine, InteractionShowcaseRuntimeKeys.C3HostilePolymorphActive),
                HostilePolymorphCount: ReadInt(engine, InteractionShowcaseRuntimeKeys.C3HostilePolymorphCount, 0),
                HostilePolymorphApplied: ReadBool(engine, InteractionShowcaseRuntimeKeys.C3HostilePolymorphApplied),
                FriendlyHasteActive: ReadBool(engine, InteractionShowcaseRuntimeKeys.C3FriendlyHasteActive),
                FriendlyHasteCount: ReadInt(engine, InteractionShowcaseRuntimeKeys.C3FriendlyHasteCount, 0),
                FriendlyHasteApplied: ReadBool(engine, InteractionShowcaseRuntimeKeys.C3FriendlyHasteApplied),
                CastSubmitted: ReadBool(engine, InteractionShowcaseRuntimeKeys.CastSubmitted),
                LastAttemptTargetName: ReadString(engine, InteractionShowcaseRuntimeKeys.LastAttemptTargetName, string.Empty));
        }

        private static string ReadString(GameEngine engine, string key, string fallback)
        {
            return engine.GlobalContext.TryGetValue(key, out var value) && value is string text
                ? text
                : fallback;
        }

        private static int ReadInt(GameEngine engine, string key, int fallback)
        {
            return engine.GlobalContext.TryGetValue(key, out var value) && value is int number
                ? number
                : fallback;
        }

        private static float ReadFloat(GameEngine engine, string key)
        {
            return engine.GlobalContext.TryGetValue(key, out var value) && value is float number
                ? number
                : 0f;
        }

        private static bool ReadBool(GameEngine engine, string key)
        {
            return engine.GlobalContext.TryGetValue(key, out var value) && value is bool flag && flag;
        }

        private static void InstallDummyInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
            engine.SetService(Ludots.Core.Scripting.CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(Ludots.Core.Scripting.CoreServiceKeys.UiCaptured, false);
        }

        private static string FindRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(dir))
            {
                string candidate = Path.Combine(dir, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("Could not locate repo root.");
        }

        private readonly record struct C3Snapshot(
            int Tick,
            string Stage,
            float Mana,
            float HostileMoveSpeed,
            float FriendlyMoveSpeed,
            bool HostilePolymorphActive,
            int HostilePolymorphCount,
            bool HostilePolymorphApplied,
            bool FriendlyHasteActive,
            int FriendlyHasteCount,
            bool FriendlyHasteApplied,
            bool CastSubmitted,
            string LastAttemptTargetName);

        private sealed class ScenarioRuntime : IDisposable
        {
            public ScenarioRuntime(GameEngine engine)
            {
                Engine = engine;
            }

            public GameEngine Engine { get; }

            public void Dispose()
            {
                Engine.Dispose();
            }
        }

        private sealed class NullInputBackend : IInputBackend
        {
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => false;
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable)
            {
            }

            public void SetIMECandidatePosition(int x, int y)
            {
            }

            public string GetCharBuffer() => string.Empty;

            public void Vibrate(int playerIndex, float lowFrequency, float highFrequency, float durationSeconds)
            {
            }

            public string[] GetConnectedGamepads() => Array.Empty<string>();
        }
    }
}
