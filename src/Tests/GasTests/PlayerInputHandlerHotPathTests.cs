using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class PlayerInputHandlerHotPathTests
    {
        [Test]
        public void PlayerInputHandler_CompiledCompositeAndProcessors_PreserveBehavior()
        {
            var backend = new StubInputBackend();
            var config = new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "Move", Type = InputActionType.Axis2D },
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Gameplay",
                        Priority = 10,
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
                                    new() { Type = "Scale", Parameters = new List<InputParameterDef> { new() { Name = "Factor", Value = 2f } } },
                                }
                            }
                        }
                    }
                }
            };

            var handler = new PlayerInputHandler(backend, config);
            handler.PushContext("Gameplay");

            backend.Buttons["<Keyboard>/w"] = true;
            backend.Buttons["<Keyboard>/d"] = true;

            handler.Update();
            var move = handler.ReadAction<Vector2>("Move");

            Assert.That(move.X, Is.EqualTo(1.4142135f).Within(0.01f));
            Assert.That(move.Y, Is.EqualTo(1.4142135f).Within(0.01f));
        }

        private sealed class StubInputBackend : IInputBackend
        {
            public Dictionary<string, bool> Buttons { get; } = new();

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => Buttons.TryGetValue(devicePath, out var down) && down;
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }
    }
}
