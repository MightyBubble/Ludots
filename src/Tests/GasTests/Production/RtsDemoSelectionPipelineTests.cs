using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace GasTests.Production
{
    public sealed class RtsDemoSelectionPipelineTests
    {
        [Test]
        public void RtsDemo_MapLoad_InstallsSelectionPipeline_And_ClickSelectsOwnUnit()
        {
            using var engine = CreateEngine(new[] { "LudotsCoreMod", "CoreInputMod", "RtsDemoMod" });
            var input = engine.GetService(CoreServiceKeys.InputHandler)!;
            var projector = new StubScreenProjector();
            engine.SetService(CoreServiceKeys.ScreenProjector, projector);

            engine.Start();
            engine.LoadMap("rts_entry");
            Tick(engine, input, 6);

            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            Assert.That(engine.GetService(CoreServiceKeys.SelectionProfileRegistry)?.Get("Rts"), Is.Not.Null);
            Assert.That(engine.GlobalContext.TryGetValue(CoreServiceKeys.ActiveSelectionProfileId.Name, out var profileIdObj), Is.True);
            Assert.That(profileIdObj, Is.EqualTo("Rts"));
            Assert.That(engine.GlobalContext.TryGetValue(CoreServiceKeys.SelectionInputHandler.Name, out var handlerObj), Is.True);
            Assert.That(handlerObj, Is.TypeOf<ScreenSelectionInputHandler>());
            Assert.That(engine.GlobalContext.TryGetValue(CoreServiceKeys.SelectionCandidatePolicy.Name, out var policyObj), Is.True);
            Assert.That(policyObj, Is.Not.Null);
            Assert.That(engine.GetService(CoreServiceKeys.SelectionInteractionState), Is.Not.Null);

            var friendly = FindFirstOwnedUnit(engine.World);
            var position = engine.World.Get<WorldPositionCm>(friendly);
            Vector2 pointer = projector.WorldToScreen(WorldUnits.WorldCmToVisualMeters(position.Value));

            input.InjectAction("PointerPos", new Vector3(pointer, 0f));
            input.Update();
            engine.Tick(1f / 60f);

            input.InjectButtonPress("Select");
            input.Update();
            engine.Tick(1f / 60f);

            input.InjectButtonRelease("Select");
            input.Update();
            engine.Tick(1f / 60f);
            Tick(engine, input, 2);

            var controller = (Entity)engine.GlobalContext[CoreServiceKeys.LocalPlayerEntity.Name];
            var selection = engine.World.Get<SelectionBuffer>(controller);
            Assert.That(selection.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(engine.World.IsAlive(selection.Primary), Is.True);
            Assert.That(selection.Primary, Is.Not.EqualTo(controller));
            Assert.That(engine.World.Has<SelectedTag>(selection.Primary), Is.True);
            Assert.That(engine.World.TryGet(selection.Primary, out PlayerOwner owner) && owner.PlayerId == 1, Is.True);
        }

        private static Entity FindFirstOwnedUnit(World world)
        {
            var query = new QueryDescription().WithAll<WorldPositionCm, PlayerOwner>();
            Entity found = default;
            world.Query(in query, (Entity entity, ref WorldPositionCm _, ref PlayerOwner owner) =>
            {
                if (owner.PlayerId == 1 && found == default)
                {
                    found = entity;
                }
            });

            if (!world.IsAlive(found))
            {
                throw new InvalidOperationException("Could not locate an owned RTS unit to select.");
            }

            return found;
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

        private static void Tick(GameEngine engine, PlayerInputHandler input, int frames)
        {
            var stepPolicy = engine.GetService(CoreServiceKeys.GasClockStepPolicy);
            for (int i = 0; i < frames; i++)
            {
                input.Update();
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

        private sealed class StubScreenProjector : IScreenProjector
        {
            public Vector2 WorldToScreen(Vector3 worldPosition)
            {
                return new Vector2(worldPosition.X * 20f + 400f, worldPosition.Z * 20f + 300f);
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


