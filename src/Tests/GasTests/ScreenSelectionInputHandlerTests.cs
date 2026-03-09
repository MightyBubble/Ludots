using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Ludots.Core.Config;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class ScreenSelectionInputHandlerTests
    {
        [Test]
        public void ScreenSelectionInputHandler_RectangleDrag_EnqueuesRectangleCommand()
        {
            var input = CreateInput();
            var profile = new SelectionProfile
            {
                Id = "Test",
                DragThresholdPx = 6f,
                DragSelectionShape = SelectionPreviewShapeKind.Rectangle,
            };
            var globals = CreateGlobals(profile);
            var interaction = new SelectionInteractionState();
            var handler = new ScreenSelectionInputHandler(globals, input, interaction);

            DriveFrame(input, handler, new Vector2(10f, 10f), selectDown: true);
            DriveFrame(input, handler, new Vector2(70f, 60f), selectDown: true);
            Assert.That(interaction.IsDragging, Is.True);
            Assert.That(interaction.PreviewShape, Is.EqualTo(SelectionPreviewShapeKind.Rectangle));

            DriveFrame(input, handler, new Vector2(70f, 60f), selectDown: false);

            Assert.That(handler.Poll(out var command), Is.True);
            Assert.That(command.Kind, Is.EqualTo(SelectionCommandKind.SelectInRectangle));
            Assert.That(command.ApplyMode, Is.EqualTo(SelectionApplyMode.Replace));
            Assert.That(command.RectangleMinScreen, Is.EqualTo(new Vector2(10f, 10f)));
            Assert.That(command.RectangleMaxScreen, Is.EqualTo(new Vector2(70f, 60f)));
        }

        [Test]
        public void ScreenSelectionInputHandler_PolygonDrag_EnqueuesPolygonCommand()
        {
            var input = CreateInput();
            var profile = new SelectionProfile
            {
                Id = "Test",
                DragThresholdPx = 4f,
                DragSelectionShape = SelectionPreviewShapeKind.Polygon,
                PolygonPointSpacingPx = 5f,
            };
            var globals = CreateGlobals(profile);
            var interaction = new SelectionInteractionState();
            var handler = new ScreenSelectionInputHandler(globals, input, interaction);

            DriveFrame(input, handler, new Vector2(10f, 10f), selectDown: true);
            DriveFrame(input, handler, new Vector2(40f, 10f), selectDown: true);
            DriveFrame(input, handler, new Vector2(40f, 40f), selectDown: true);
            DriveFrame(input, handler, new Vector2(10f, 40f), selectDown: true);
            Assert.That(interaction.IsDragging, Is.True);
            Assert.That(interaction.PreviewShape, Is.EqualTo(SelectionPreviewShapeKind.Polygon));
            Assert.That(interaction.PolygonScreen.Length, Is.GreaterThanOrEqualTo(3));

            DriveFrame(input, handler, new Vector2(10f, 40f), selectDown: false);

            Assert.That(handler.Poll(out var command), Is.True);
            Assert.That(command.Kind, Is.EqualTo(SelectionCommandKind.SelectInPolygon));
            Assert.That(command.PolygonScreen, Is.Not.Null);
            Assert.That(command.PolygonScreen!.Length, Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void ScreenSelectionInputHandler_AddModifier_EnqueuesAddPointCommand()
        {
            var input = CreateInput();
            var profile = new SelectionProfile
            {
                Id = "Test",
                DragThresholdPx = 100f,
                DragSelectionShape = SelectionPreviewShapeKind.Rectangle,
                EnableAdditiveSelection = true,
                EnableToggleSelection = false,
                AddModifierActionId = "QueueModifier",
            };
            var globals = CreateGlobals(profile);
            var interaction = new SelectionInteractionState();
            var handler = new ScreenSelectionInputHandler(globals, input, interaction);

            DriveFrame(input, handler, new Vector2(24f, 32f), selectDown: true, addDown: true);
            DriveFrame(input, handler, new Vector2(24f, 32f), selectDown: false, addDown: true);

            Assert.That(handler.Poll(out var command), Is.True);
            Assert.That(command.Kind, Is.EqualTo(SelectionCommandKind.SelectAtPoint));
            Assert.That(command.ApplyMode, Is.EqualTo(SelectionApplyMode.Add));
        }

        [Test]
        public void ScreenSelectionInputHandler_DoubleClick_EnqueuesExpandedPointCommand()
        {
            var input = CreateInput();
            var profile = new SelectionProfile
            {
                Id = "Test",
                DragThresholdPx = 100f,
                DragSelectionShape = SelectionPreviewShapeKind.Rectangle,
                PointExpansion = SelectionPointExpansionKind.SameClassDoubleClick,
                DoubleClickWindowSec = 0.40f,
                DoubleClickMaxDistancePx = 16f,
            };
            var globals = CreateGlobals(profile);
            var interaction = new SelectionInteractionState();
            var handler = new ScreenSelectionInputHandler(globals, input, interaction);

            DriveFrame(input, handler, new Vector2(20f, 20f), selectDown: true);
            DriveFrame(input, handler, new Vector2(20f, 20f), selectDown: false);
            Assert.That(handler.Poll(out var first), Is.True);
            Assert.That(first.ExpandSameClassFromResolvedCandidate, Is.False);

            DriveFrame(input, handler, new Vector2(24f, 24f), selectDown: true, dt: 0.10f);
            DriveFrame(input, handler, new Vector2(24f, 24f), selectDown: false, dt: 0.10f);
            Assert.That(handler.Poll(out var second), Is.True);
            Assert.That(second.Kind, Is.EqualTo(SelectionCommandKind.SelectAtPoint));
            Assert.That(second.ExpandSameClassFromResolvedCandidate, Is.True);
        }

        private static void DriveFrame(
            PlayerInputHandler input,
            ScreenSelectionInputHandler handler,
            Vector2 pointer,
            bool selectDown,
            bool addDown = false,
            bool toggleDown = false,
            float dt = 1f / 60f)
        {
            input.InjectAction("PointerPos", new Vector3(pointer, 0f));
            if (selectDown)
            {
                input.InjectButtonPress("Select");
            }

            if (addDown)
            {
                input.InjectButtonPress("QueueModifier");
            }

            if (toggleDown)
            {
                input.InjectButtonPress("PrecisionModifier");
            }

            input.Update();
            handler.Update(dt);
        }

        private static Dictionary<string, object> CreateGlobals(SelectionProfile profile)
        {
            return new Dictionary<string, object>
            {
                [CoreServiceKeys.ActiveSelectionProfileId.Name] = profile.Id,
                [CoreServiceKeys.SelectionProfileRegistry.Name] = CreateRegistry(profile),
                [CoreServiceKeys.UiCaptured.Name] = false,
            };
        }

        private static SelectionProfileRegistry CreateRegistry(params SelectionProfile[] profiles)
        {
            var registry = new SelectionProfileRegistry(null!);
            var field = typeof(DataRegistry<SelectionProfile>).GetField("_data", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("DataRegistry<SelectionProfile>._data not found.");
            var data = (Dictionary<string, SelectionProfile>)field.GetValue(registry)!;
            foreach (var profile in profiles)
            {
                data[profile.Id] = profile;
            }

            return registry;
        }

        private static PlayerInputHandler CreateInput()
        {
            return new PlayerInputHandler(new NullInputBackend(), new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new InputActionDef { Id = "PointerPos", Type = InputActionType.Axis2D, Name = "Pointer" },
                    new InputActionDef { Id = "Select", Type = InputActionType.Button, Name = "Select" },
                    new InputActionDef { Id = "Cancel", Type = InputActionType.Button, Name = "Cancel" },
                    new InputActionDef { Id = "QueueModifier", Type = InputActionType.Button, Name = "Queue" },
                    new InputActionDef { Id = "PrecisionModifier", Type = InputActionType.Button, Name = "Toggle" },
                }
            });
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
