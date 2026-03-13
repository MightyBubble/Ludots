using System;
using System.IO;
using System.Numerics;
using System.Reflection;
using Arch.Core;
using Arch.System;
using InteractionShowcaseMod;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [NonParallelizable]
    public sealed class InteractionShowcaseScaffoldTests
    {
        private const float DeltaTime = 1f / 60f;

        [Test]
        public unsafe void AbilityActivationPreconditionEvaluator_ThrowsOnUnknownComparison()
        {
            using var world = World.Create();
            int manaId = AttributeRegistry.Register("Mana");
            Entity actor = world.Create(new AttributeBuffer());
            ref var attributes = ref world.Get<AttributeBuffer>(actor);
            attributes.SetBase(manaId, 100f);

            var preconditions = default(AbilityAttributePreconditions);
            Assert.That(
                preconditions.TryAdd(
                    manaId,
                    50f,
                    AbilityAttributeComparison.GreaterOrEqual,
                    AbilityCastFailReason.InsufficientResource),
                Is.True);

            preconditions.Comparisons[0] = byte.MaxValue;

            Type evaluatorType = typeof(GameEngine).Assembly.GetType(
                "Ludots.Core.Gameplay.GAS.AbilityActivationPreconditionEvaluator",
                throwOnError: true)!;
            MethodInfo tryPass = evaluatorType.GetMethod(
                "TryPass",
                BindingFlags.Public | BindingFlags.Static)!;

            object?[] args =
            {
                world,
                actor,
                preconditions,
                null,
                null,
                null,
                null,
            };

            TargetInvocationException? ex = Assert.Throws<TargetInvocationException>(() => tryPass.Invoke(null, args));
            Assert.That(ex?.InnerException, Is.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(ex?.InnerException?.Message, Does.Contain(nameof(AbilityAttributeComparison)));
        }

        [Test]
        public void InteractionShowcaseGasEventTapSystem_DoesNotRestampOrSkipFailureEvents()
        {
            using var runtime = StartScenario();
            GasPresentationEventBuffer? eventBuffer = runtime.Engine.GetService(CoreServiceKeys.GasPresentationEventBuffer);
            Assert.That(eventBuffer, Is.Not.Null);

            int abilityId = AbilityIdRegistry.GetId(InteractionShowcaseIds.B1SelfBuffAbilityId);
            int c2AbilityId = AbilityIdRegistry.GetId(InteractionShowcaseIds.C2FriendlyUnitHealAbilityId);
            int manaId = AttributeRegistry.Register("Mana");
            using ISystem<float> tapSystem = CreateTapSystem(runtime.Engine);

            eventBuffer!.Publish(new GasPresentationEvent
            {
                AbilityId = abilityId,
                Kind = GasPresentationEventKind.CastFailed,
                FailReason = AbilityCastFailReason.BlockedByTag,
            });

            tapSystem.Update(DeltaTime);
            int firstTick = ReadInt(runtime.Engine, InteractionShowcaseRuntimeKeys.LastCastFailTick, -1);
            string firstReason = ReadString(runtime.Engine, InteractionShowcaseRuntimeKeys.LastCastFailReason, string.Empty);

            runtime.Engine.GameSession.FixedUpdate();
            tapSystem.Update(DeltaTime);

            Assert.That(ReadInt(runtime.Engine, InteractionShowcaseRuntimeKeys.LastCastFailTick, -1), Is.EqualTo(firstTick));
            Assert.That(ReadString(runtime.Engine, InteractionShowcaseRuntimeKeys.LastCastFailReason, string.Empty), Is.EqualTo(firstReason));

            eventBuffer.Clear();
            runtime.Engine.GameSession.FixedUpdate();
            eventBuffer.Publish(new GasPresentationEvent
            {
                AbilityId = abilityId,
                Kind = GasPresentationEventKind.CastFailed,
                FailReason = AbilityCastFailReason.InsufficientResource,
                AttributeId = manaId,
                Delta = 50f,
            });

            tapSystem.Update(DeltaTime);

            Assert.That(ReadInt(runtime.Engine, InteractionShowcaseRuntimeKeys.LastCastFailTick, -1), Is.GreaterThan(firstTick));
            Assert.That(ReadString(runtime.Engine, InteractionShowcaseRuntimeKeys.LastCastFailReason, string.Empty), Is.EqualTo("InsufficientResource"));
            Assert.That(ReadString(runtime.Engine, InteractionShowcaseRuntimeKeys.LastCastFailAttribute, string.Empty), Is.EqualTo("Mana"));
            Assert.That(ReadFloat(runtime.Engine, InteractionShowcaseRuntimeKeys.LastCastFailDelta, 0f), Is.EqualTo(50f).Within(0.001f));

            Entity target = runtime.Engine.World.Create(new Name
            {
                Value = InteractionShowcaseIds.C2HostileTargetName
            });

            eventBuffer.Clear();
            runtime.Engine.GameSession.FixedUpdate();
            eventBuffer.Publish(new GasPresentationEvent
            {
                AbilityId = c2AbilityId,
                Kind = GasPresentationEventKind.CastFailed,
                Target = target,
                FailReason = AbilityCastFailReason.NotAlive,
            });

            tapSystem.Update(DeltaTime);

            Assert.That(
                ReadString(runtime.Engine, InteractionShowcaseRuntimeKeys.LastAttemptTargetName, string.Empty),
                Is.EqualTo(InteractionShowcaseIds.C2HostileTargetName));
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
            engine.LoadMap(InteractionShowcaseIds.B1SelfBuffMapId);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0), "Map load should not emit trigger errors.");
            return new ScenarioRuntime(engine);
        }

        private static ISystem<float> CreateTapSystem(GameEngine engine)
        {
            Type tapSystemType = typeof(InteractionShowcaseIds).Assembly.GetType(
                "InteractionShowcaseMod.Systems.InteractionShowcaseGasEventTapSystem",
                throwOnError: true)!;
            return (ISystem<float>)Activator.CreateInstance(tapSystemType, engine)!;
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

        private static float ReadFloat(GameEngine engine, string key, float fallback)
        {
            return engine.GlobalContext.TryGetValue(key, out var value) && value is float number
                ? number
                : fallback;
        }

        private static void InstallDummyInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
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
