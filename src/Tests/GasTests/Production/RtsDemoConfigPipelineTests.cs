using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace GasTests.Production
{
    public sealed class RtsDemoConfigPipelineTests
    {
        private const string ActiveMappingKey = "CoreInputMod.ActiveInputOrderMapping";
        private const string RtsPresetId = "Rts";
        private const string RtsInputContextId = "Rts_Gameplay";

        [Test]
        public void RtsDemo_MapLoad_UsesCoreCameraPreset_And_RtsInputSelectionPipeline()
        {
            using var engine = CreateEngine(new[] { "LudotsCoreMod", "CoreInputMod", "RtsDemoMod" });

            engine.Start();
            engine.LoadMap("rts_entry");
            Tick(engine, 6);

            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0), "Expected RTS demo startup/load without trigger errors.");
            Assert.That(engine.GameSession.Camera.ActivePreset?.Id, Is.EqualTo(RtsPresetId));

            var camera = engine.GameSession.Camera.State;
            Assert.That(camera.DistanceCm, Is.EqualTo(4000f).Within(0.001f));
            Assert.That(camera.Pitch, Is.EqualTo(56f).Within(0.001f));
            Assert.That(camera.FovYDeg, Is.EqualTo(50f).Within(0.001f));
            Assert.That(camera.Yaw, Is.EqualTo(180f).Within(0.001f));

            Assert.That(engine.GetService(CoreServiceKeys.SelectionProfileRegistry)?.Get(RtsPresetId), Is.Not.Null);
            Assert.That(engine.GlobalContext.TryGetValue(CoreServiceKeys.ActiveSelectionProfileId.Name, out var activeProfileObj), Is.True);
            Assert.That(activeProfileObj, Is.EqualTo(RtsPresetId));

            var input = engine.GetService(CoreServiceKeys.InputHandler);
            Assert.That(input, Is.Not.Null);
            var activeContexts = GetActiveContextIds(input!);
            Assert.That(activeContexts, Does.Contain(RtsInputContextId));
            Assert.That(activeContexts.Count(id => string.Equals(id, RtsInputContextId, StringComparison.Ordinal)), Is.EqualTo(1));
            Assert.That(activeContexts.Count, Is.EqualTo(1), "RTS demo should own a single explicit gameplay context.");

            Assert.That(engine.GlobalContext.TryGetValue(ActiveMappingKey, out var mappingObj), Is.True);
            Assert.That(mappingObj, Is.TypeOf<InputOrderMappingSystem>());
            var mapping = (InputOrderMappingSystem)mappingObj!;
            Assert.That(mapping.InteractionMode, Is.EqualTo(InteractionModeType.SmartCast));
        }

        private static GameEngine CreateEngine(string[] mods)
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            string modsRoot = Path.Combine(repoRoot, "mods");
            var modPaths = mods.Select(mod => Path.Combine(modsRoot, mod)).ToList();

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallDummyInput(engine);
            return engine;
        }

        private static void Tick(GameEngine engine, int frames)
        {
            var stepPolicy = engine.GetService(CoreServiceKeys.GasClockStepPolicy);
            for (int i = 0; i < frames; i++)
            {
                if (stepPolicy.Mode == GasStepMode.Manual)
                {
                    stepPolicy.RequestStep(1);
                }

                engine.Tick(1f / 60f);
            }
        }

        private static void InstallDummyInput(GameEngine engine)
        {
            InputConfigRoot inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
        }

        private static IReadOnlyList<string> GetActiveContextIds(PlayerInputHandler input)
        {
            var field = typeof(PlayerInputHandler).GetField("_activeContexts", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("PlayerInputHandler._activeContexts not found.");
            var list = field.GetValue(input) as IEnumerable
                ?? throw new InvalidOperationException("PlayerInputHandler._activeContexts is not enumerable.");

            var result = new List<string>();
            foreach (var item in list)
            {
                if (item == null)
                {
                    continue;
                }

                var idProp = item.GetType().GetProperty("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("Compiled input context Id property not found.");
                if (idProp.GetValue(item) is string id)
                {
                    result.Add(id);
                }
            }

            return result;
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

        private static string FindRepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
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
    }
}
