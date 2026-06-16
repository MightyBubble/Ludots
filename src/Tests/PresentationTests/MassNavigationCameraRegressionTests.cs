using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using MassNavigationMod.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class MassNavigationCameraRegressionTests
    {
        private const string TestInputBackendKey = "Tests.MassNavigationCamera.InputBackend";
        private const float FrameSeconds = 1f / 60f;

        [Test]
        public void MassNavigationCamera_HoldingW_RenderCameraUsesSmoothInterpolatedTarget()
        {
            using GameEngine engine = CreateEngine();
            engine.LoadMap("mass_navigation");
            Tick(engine, 8);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            var backend = GetInputBackend(engine);
            Vector2 initialTarget = engine.GameSession.Camera.State.TargetCm;
            Assert.That(engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId, Is.EqualTo("Camera.Profile.MassNavigationTactical"));
            Assert.That(engine.GlobalContext.ContainsKey(CoreServiceKeys.VirtualCameraRequest.Name), Is.False);
            Assert.That(engine.GlobalContext.ContainsKey(CoreServiceKeys.CameraPoseRequest.Name), Is.False);

            backend.SetButton("<Keyboard>/w", true);
            var samples = new List<CameraSample>(capacity: 40);
            for (int i = 0; i < 40; i++)
            {
                Tick(engine, 1);
                samples.Add(CaptureSample(engine, simulation));
            }

            backend.SetButton("<Keyboard>/w", false);
            Tick(engine, 1);

            Assert.That(samples[^1].CurrentTarget, Is.Not.EqualTo(initialTarget),
                "W input must move the active tactical camera through the formal 3C path.");

            for (int i = 0; i < samples.Count; i++)
            {
                Assert.That(samples[i].HasVirtualCameraRequest, Is.False, FormatSamples(samples));
                Assert.That(samples[i].HasCameraPoseRequest, Is.False, FormatSamples(samples));
                Assert.That(samples[i].SolverCenterX, Is.EqualTo(0f).Within(0.5f), FormatSamples(samples));
                Assert.That(samples[i].SolverCenterY, Is.EqualTo(0f).Within(0.5f), FormatSamples(samples));
                Assert.That(Vector2.Distance(samples[i].InterpolatedTarget, samples[i].PresenterTarget), Is.LessThan(0.5f), FormatSamples(samples));
                Assert.That(Vector2.Distance(samples[i].InterpolatedTarget, samples[i].FlowWorkAreaCenter), Is.LessThan(1.5f), FormatSamples(samples));
            }

            bool sawInterpolationBetweenLogicTicks = false;
            for (int i = 0; i < samples.Count; i++)
            {
                float distanceToPrevious = Vector2.Distance(samples[i].InterpolatedTarget, samples[i].PreviousTarget);
                float distanceToCurrent = Vector2.Distance(samples[i].InterpolatedTarget, samples[i].CurrentTarget);
                if (distanceToPrevious > 0.5f && distanceToCurrent > 0.5f)
                {
                    sawInterpolationBetweenLogicTicks = true;
                    break;
                }
            }

            Assert.That(sawInterpolationBetweenLogicTicks, Is.True, FormatSamples(samples));

            for (int i = 1; i < samples.Count; i++)
            {
                Vector2 delta = samples[i].InterpolatedTarget - samples[i - 1].InterpolatedTarget;
                if (delta.Length() <= 0.5f)
                {
                    continue;
                }

                Assert.That(Vector2.Dot(delta, samples[^1].InterpolatedTarget - samples[0].InterpolatedTarget), Is.GreaterThan(0f), FormatSamples(samples));
                Assert.That(Vector2.Distance(samples[i].InterpolatedTarget, initialTarget), Is.GreaterThanOrEqualTo(Vector2.Distance(samples[i - 1].InterpolatedTarget, initialTarget) - 0.001f), FormatSamples(samples));
            }
        }

        private static GameEngine CreateEngine()
        {
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(MassNavigationDependencyPaths(), Path.Combine(FindRepoRoot(), "assets"));
            InstallInput(engine);
            HeadlessPresentationTestHost.Install(engine, new CameraCullingFocusOverride());
            engine.Start();
            return engine;
        }

        private static List<string> MassNavigationDependencyPaths()
        {
            string repoRoot = FindRepoRoot();
            string modsRoot = Path.Combine(repoRoot, "mods");
            return new List<string>
            {
                Path.Combine(modsRoot, "LudotsCoreMod"),
                Path.Combine(modsRoot, "CoreInputMod"),
                Path.Combine(modsRoot, "capabilities", "camera", "CameraProfilesMod"),
                Path.Combine(modsRoot, "capabilities", "navigation", "MassNavigationMod"),
            };
        }

        private static void InstallInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var backend = new TestInputBackend();
            var inputHandler = new PlayerInputHandler(backend, inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.GlobalContext[TestInputBackendKey] = backend;
        }

        private static void Tick(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(FrameSeconds);
                HeadlessPresentationTestHost.UpdateCamera(engine);
            }
        }

        private static CameraSample CaptureSample(GameEngine engine, MassNavigationSimulationRuntime simulation)
        {
            CameraState state = engine.GameSession.Camera.State;
            CameraState previous = engine.GameSession.Camera.PreviousState;
            float alpha = engine.GetService(CoreServiceKeys.PresentationFrameSetup)?.GetInterpolationAlpha() ?? 1f;
            CameraStateSnapshot interpolated = engine.GameSession.Camera.GetInterpolatedState(alpha);
            CameraPresenter presenter = HeadlessPresentationTestHost.GetCameraPresenter(engine)
                ?? throw new InvalidOperationException("Headless camera presenter is missing.");
            Vector2 presenterTarget = new(
                presenter.CurrentTargetPosition.X * WorldUnits.CmPerMeter,
                presenter.CurrentTargetPosition.Z * WorldUnits.CmPerMeter);

            return new CameraSample(
                state.TargetCm,
                previous.TargetCm,
                interpolated.TargetCm,
                presenterTarget,
                alpha,
                simulation.SolverWindowCenterXCm,
                simulation.SolverWindowCenterYCm,
                new Vector2(simulation.FlowWorkAreaCenterXCm, simulation.FlowWorkAreaCenterYCm),
                engine.GlobalContext.ContainsKey(CoreServiceKeys.VirtualCameraRequest.Name),
                engine.GlobalContext.ContainsKey(CoreServiceKeys.CameraPoseRequest.Name));
        }

        private static TestInputBackend GetInputBackend(GameEngine engine)
        {
            return engine.GlobalContext[TestInputBackendKey] as TestInputBackend
                ?? throw new InvalidOperationException("MassNavigation camera test input backend is missing.");
        }

        private static MassNavigationSimulationRuntime RequireSimulation(GameEngine engine)
        {
            return engine.GetService(MassNavigationMod.MassNavigationKeys.SimulationRuntime)
                ?? throw new InvalidOperationException("MassNavigationSimulationRuntime is missing.");
        }

        private static string FormatSamples(IReadOnlyList<CameraSample> samples)
        {
            var lines = new List<string>(samples.Count + 1)
            {
                "MassNavigation camera samples:"
            };
            for (int i = 0; i < samples.Count; i++)
            {
                CameraSample sample = samples[i];
                lines.Add(
                    $"{i}: alpha={sample.Alpha:0.###} current=({sample.CurrentTarget.X:0.###},{sample.CurrentTarget.Y:0.###}) prev=({sample.PreviousTarget.X:0.###},{sample.PreviousTarget.Y:0.###}) interpolated=({sample.InterpolatedTarget.X:0.###},{sample.InterpolatedTarget.Y:0.###}) presenter=({sample.PresenterTarget.X:0.###},{sample.PresenterTarget.Y:0.###}) flow=({sample.FlowWorkAreaCenter.X:0.###},{sample.FlowWorkAreaCenter.Y:0.###}) solver=({sample.SolverCenterX:0.###},{sample.SolverCenterY:0.###}) requests=({sample.HasVirtualCameraRequest},{sample.HasCameraPoseRequest})");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                var srcDir = Path.Combine(dir.FullName, "src");
                var assetsDir = Path.Combine(dir.FullName, "assets");
                if (Directory.Exists(srcDir) && Directory.Exists(assetsDir))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
        }

        private readonly record struct CameraSample(
            Vector2 CurrentTarget,
            Vector2 PreviousTarget,
            Vector2 InterpolatedTarget,
            Vector2 PresenterTarget,
            float Alpha,
            float SolverCenterX,
            float SolverCenterY,
            Vector2 FlowWorkAreaCenter,
            bool HasVirtualCameraRequest,
            bool HasCameraPoseRequest);

        private sealed class TestInputBackend : IInputBackend
        {
            private readonly Dictionary<string, bool> _buttons = new(StringComparer.Ordinal);

            public void SetButton(string path, bool isDown)
            {
                _buttons[path] = isDown;
            }

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => _buttons.TryGetValue(devicePath, out bool isDown) && isDown;
            public Vector2 GetMousePosition() => new(960f, 540f);
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }
    }
}
