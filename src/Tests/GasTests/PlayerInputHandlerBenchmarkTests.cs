using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [NonParallelizable]
    public sealed class PlayerInputHandlerBenchmarkTests
    {
        [Test]
        public void Benchmark_PlayerInputHandler_Update_CompiledContexts_ZeroAlloc()
        {
            var backend = new StubInputBackend();
            SeedBackend(backend);

            var config = CreateBenchmarkConfig();
            var handler = new PlayerInputHandler(backend, config);
            handler.PushContext("Gameplay");
            handler.PushContext("Camera");

            for (int i = 0; i < 256; i++)
            {
                handler.Update();
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.GetAllocatedBytesForCurrentThread();

            const int iterations = 200_000;
            long before = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                handler.Update();
            }
            sw.Stop();
            long after = GC.GetAllocatedBytesForCurrentThread();

            double perUpdateUs = sw.Elapsed.TotalMilliseconds * 1000.0 / iterations;
            double updatesPerSecond = iterations / sw.Elapsed.TotalSeconds;
            int bindingCount = CountBindings(config);

            Console.WriteLine("[InputPerf] PlayerInputHandler.Update:");
            Console.WriteLine($"  Contexts: {config.Contexts.Count}");
            Console.WriteLine($"  Actions: {config.Actions.Count}");
            Console.WriteLine($"  Bindings: {bindingCount}");
            Console.WriteLine($"  Iterations: {iterations}");
            Console.WriteLine($"  TotalMs: {sw.Elapsed.TotalMilliseconds:F2}");
            Console.WriteLine($"  PerUpdateUs: {perUpdateUs:F4}");
            Console.WriteLine($"  UpdatesPerSec: {updatesPerSecond:F0}");
            Console.WriteLine($"  AllocatedBytes(CurrentThread): {after - before}");

            var move = handler.ReadAction<Vector2>("Move");
            Assert.That(move.X, Is.GreaterThan(0f));
            Assert.That(move.Y, Is.GreaterThan(0f));
            Assert.That(handler.IsDown("Skill1"), Is.True);
            Assert.That(handler.IsDown("Select"), Is.True);
            Assert.That(after - before, Is.LessThanOrEqualTo(64));
        }

        private static InputConfigRoot CreateBenchmarkConfig()
        {
            return new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "Move", Type = InputActionType.Axis2D },
                    new() { Id = "Look", Type = InputActionType.Axis2D },
                    new() { Id = "Zoom", Type = InputActionType.Axis1D },
                    new() { Id = "Select", Type = InputActionType.Button },
                    new() { Id = "Confirm", Type = InputActionType.Button },
                    new() { Id = "Cancel", Type = InputActionType.Button },
                    new() { Id = "Skill1", Type = InputActionType.Button },
                    new() { Id = "Skill2", Type = InputActionType.Button },
                    new() { Id = "Skill3", Type = InputActionType.Button },
                    new() { Id = "Skill4", Type = InputActionType.Button },
                    new() { Id = "PanLeft", Type = InputActionType.Button },
                    new() { Id = "PanRight", Type = InputActionType.Button },
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Gameplay",
                        Priority = 20,
                        Bindings = new List<InputBindingDef>
                        {
                            new()
                            {
                                ActionId = "Move",
                                CompositeType = "Vector2",
                                CompositeParts = new List<InputBindingDef>
                                {
                                    new() { Path = "<Keyboard>/w" },
                                    new() { Path = "<Keyboard>/s" },
                                    new() { Path = "<Keyboard>/a" },
                                    new() { Path = "<Keyboard>/d" },
                                },
                                Processors = new List<InputModifierDef>
                                {
                                    new() { Type = "Normalize" },
                                    new() { Type = "Scale", Parameters = new List<InputParameterDef> { new() { Name = "Factor", Value = 1.5f } } },
                                }
                            },
                            new() { ActionId = "Select", Path = "<Mouse>/leftButton" },
                            new() { ActionId = "Confirm", Path = "<Keyboard>/enter" },
                            new() { ActionId = "Cancel", Path = "<Keyboard>/escape" },
                            new() { ActionId = "Skill1", Path = "<Keyboard>/q" },
                            new() { ActionId = "Skill2", Path = "<Keyboard>/w" },
                            new() { ActionId = "Skill3", Path = "<Keyboard>/e" },
                            new() { ActionId = "Skill4", Path = "<Keyboard>/r" },
                        }
                    },
                    new()
                    {
                        Id = "Camera",
                        Priority = 10,
                        Bindings = new List<InputBindingDef>
                        {
                            new() { ActionId = "Look", Path = "<Mouse>/Pos" },
                            new() { ActionId = "Zoom", Path = "<Mouse>/ScrollY" },
                            new() { ActionId = "PanLeft", Path = "<Keyboard>/leftArrow" },
                            new() { ActionId = "PanRight", Path = "<Keyboard>/rightArrow" },
                        }
                    }
                }
            };
        }

        private static int CountBindings(InputConfigRoot config)
        {
            int count = 0;
            for (int i = 0; i < config.Contexts.Count; i++)
            {
                var bindings = config.Contexts[i].Bindings;
                count += bindings.Count;
                for (int j = 0; j < bindings.Count; j++)
                {
                    count += bindings[j].CompositeParts?.Count ?? 0;
                }
            }

            return count;
        }

        private static void SeedBackend(StubInputBackend backend)
        {
            backend.Buttons["<Keyboard>/w"] = true;
            backend.Buttons["<Keyboard>/d"] = true;
            backend.Buttons["<Mouse>/leftButton"] = true;
            backend.Buttons["<Keyboard>/enter"] = true;
            backend.Buttons["<Keyboard>/q"] = true;
            backend.Buttons["<Keyboard>/e"] = true;
            backend.Buttons["<Keyboard>/leftArrow"] = true;
            backend.MousePosition = new Vector2(640f, 360f);
            backend.MouseWheel = 1f;
        }

        private sealed class StubInputBackend : IInputBackend
        {
            public Dictionary<string, bool> Buttons { get; } = new();
            public Vector2 MousePosition { get; set; }
            public float MouseWheel { get; set; }

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => Buttons.TryGetValue(devicePath, out var down) && down;
            public Vector2 GetMousePosition() => MousePosition;
            public float GetMouseWheel() => MouseWheel;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }
    }
}
