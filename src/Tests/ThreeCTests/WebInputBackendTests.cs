using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Adapter.Web.Protocol;
using Ludots.Adapter.Web.Services;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.UI.Input;
using NUnit.Framework;

namespace Ludots.Tests.ThreeC
{
    [TestFixture]
    public sealed class WebInputBackendTests
    {
        [Test]
        public void WebInputBackend_FastPointerTap_ProducesPressedThenReleasedAcrossFrames()
        {
            var backend = new WebInputBackend();
            var handler = CreateHandler(backend);

            backend.ApplyStateMessage(CreateInputStateMessage(buttonMask: 0, mouseX: 640f, mouseY: 360f, mouseWheel: 0f, keyBits: 0));
            backend.EnqueuePointerMessage(CreatePointerMessage(PointerAction.Down, InputProtocol.ButtonMaskLeft, 640f, 360f));
            backend.EnqueuePointerMessage(CreatePointerMessage(PointerAction.Up, 0, 640f, 360f));

            backend.AdvanceFrameInput();
            handler.Update();

            Assert.Multiple(() =>
            {
                Assert.That(handler.PressedThisFrame("Confirm"), Is.True);
                Assert.That(handler.IsDown("Confirm"), Is.True);
                Assert.That(handler.ReleasedThisFrame("Confirm"), Is.False);
                Assert.That(handler.ReadAction<Vector2>("Pointer"), Is.EqualTo(new Vector2(640f, 360f)));
            });

            backend.AdvanceFrameInput();
            handler.Update();

            Assert.Multiple(() =>
            {
                Assert.That(handler.PressedThisFrame("Confirm"), Is.False);
                Assert.That(handler.IsDown("Confirm"), Is.False);
                Assert.That(handler.ReleasedThisFrame("Confirm"), Is.True);
                Assert.That(handler.ReadAction<Vector2>("Pointer"), Is.EqualTo(new Vector2(640f, 360f)));
            });
        }

        [Test]
        public void WebInputBackend_FastKeyboardTap_ProducesPressedThenReleasedAcrossFrames()
        {
            var backend = new WebInputBackend();
            var handler = CreateHandler(backend);

            backend.ApplyStateMessage(CreateInputStateMessage(buttonMask: 0, mouseX: 640f, mouseY: 360f, mouseWheel: 0f, keyBits: 0));
            backend.ApplyStateMessage(CreateInputStateMessage(buttonMask: 0, mouseX: 640f, mouseY: 360f, mouseWheel: 0f, keyBits: 1UL << 22));
            backend.ApplyStateMessage(CreateInputStateMessage(buttonMask: 0, mouseX: 640f, mouseY: 360f, mouseWheel: 0f, keyBits: 0));

            backend.AdvanceFrameInput();
            handler.Update();
            Assert.That(handler.PressedThisFrame("MoveUp"), Is.True);
            Assert.That(handler.IsDown("MoveUp"), Is.True);

            backend.AdvanceFrameInput();
            handler.Update();
            Assert.That(handler.ReleasedThisFrame("MoveUp"), Is.True);
            Assert.That(handler.IsDown("MoveUp"), Is.False);
        }

        private static PlayerInputHandler CreateHandler(IInputBackend backend)
        {
            var config = new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "Pointer", Type = InputActionType.Axis2D },
                    new() { Id = "Confirm", Type = InputActionType.Button },
                    new() { Id = "MoveUp", Type = InputActionType.Button },
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Gameplay",
                        Priority = 10,
                        Bindings = new List<InputBindingDef>
                        {
                            new() { ActionId = "Pointer", Path = "<Mouse>/Pos" },
                            new() { ActionId = "Confirm", Path = "<Mouse>/LeftButton" },
                            new() { ActionId = "MoveUp", Path = "<Keyboard>/w" },
                        }
                    }
                }
            };

            var handler = new PlayerInputHandler(backend, config);
            handler.PushContext("Gameplay");
            return handler;
        }

        private static byte[] CreateInputStateMessage(int buttonMask, float mouseX, float mouseY, float mouseWheel, ulong keyBits)
        {
            var buffer = new byte[InputProtocol.InputStateMessageSize];
            buffer[0] = InputProtocol.MsgTypeInputState;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(InputProtocol.InputStateButtonMaskOffset, 4), buttonMask);
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(InputProtocol.InputStateMouseXOffset, 4), mouseX);
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(InputProtocol.InputStateMouseYOffset, 4), mouseY);
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(InputProtocol.InputStateMouseWheelOffset, 4), mouseWheel);
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(InputProtocol.InputStateKeyBitsOffset, 8), keyBits);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(InputProtocol.InputStateViewportWidthOffset, 4), 1280);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(InputProtocol.InputStateViewportHeightOffset, 4), 720);
            return buffer;
        }

        private static byte[] CreatePointerMessage(PointerAction action, int buttonMask, float mouseX, float mouseY)
        {
            var buffer = new byte[InputProtocol.PointerEventMessageSize];
            buffer[0] = InputProtocol.MsgTypePointerEvent;
            buffer[InputProtocol.PointerActionOffset] = (byte)action;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(InputProtocol.PointerButtonMaskOffset, 4), buttonMask);
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(InputProtocol.PointerXOffset, 4), mouseX);
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(InputProtocol.PointerYOffset, 4), mouseY);
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(InputProtocol.PointerDeltaXOffset, 4), 0f);
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(InputProtocol.PointerDeltaYOffset, 4), 0f);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(InputProtocol.PointerViewportWidthOffset, 4), 1280);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(InputProtocol.PointerViewportHeightOffset, 4), 720);
            return buffer;
        }
    }
}
