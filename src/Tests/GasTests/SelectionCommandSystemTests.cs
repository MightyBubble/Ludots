using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Input.Selection;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class SelectionCommandSystemTests
    {
        [Test]
        public void SelectionCommandSystem_SaveClearRecall_RestoresSelectionGroup()
        {
            using var world = World.Create();
            var globals = new Dictionary<string, object>();
            var controller = world.Create(new SelectionBuffer(), new SelectionGroupBuffer());
            var first = CreateSelectable(world, 100, 100, selectionClass: 1);
            var second = CreateSelectable(world, 180, 120, selectionClass: 2);

            globals[CoreServiceKeys.LocalPlayerEntity.Name] = controller;
            globals[CoreServiceKeys.ActiveSelectionProfileId.Name] = "Test";
            globals[CoreServiceKeys.SelectionInputHandler.Name] = new StubSelectionInputHandler(new[]
            {
                SelectionInputCommand.CreateRectangle(new Vector2(0f, 0f), new Vector2(50f, 50f), SelectionApplyMode.Replace),
                SelectionInputCommand.CreateSaveGroup(1),
                SelectionInputCommand.CreateClear(),
                SelectionInputCommand.CreateRecallGroup(1),
            });
            globals[CoreServiceKeys.SelectionCandidatePolicy.Name] = new StubSelectionCandidatePolicy();
            globals[CoreServiceKeys.ScreenProjector.Name] = new StubScreenProjector();

            var system = new SelectionCommandSystem(world, globals);
            system.Update(0f);

            var buffer = world.Get<SelectionBuffer>(controller);
            Assert.That(buffer.Count, Is.EqualTo(2));
            Assert.That(buffer.Contains(first), Is.True);
            Assert.That(buffer.Contains(second), Is.True);
            Assert.That(world.Has<SelectedTag>(first), Is.True);
            Assert.That(world.Has<SelectedTag>(second), Is.True);
        }

        [Test]
        public void SelectionCommandSystem_ExpandSameClass_SelectsVisibleMatches()
        {
            using var world = World.Create();
            var globals = new Dictionary<string, object>();
            var controller = world.Create(new SelectionBuffer(), new SelectionGroupBuffer());
            var first = CreateSelectable(world, 100, 100, selectionClass: 7);
            var second = CreateSelectable(world, 120, 140, selectionClass: 7);
            var third = CreateSelectable(world, 220, 240, selectionClass: 9);

            globals[CoreServiceKeys.LocalPlayerEntity.Name] = controller;
            globals[CoreServiceKeys.ActiveSelectionProfileId.Name] = "Test";
            globals[CoreServiceKeys.SelectionInputHandler.Name] = new StubSelectionInputHandler(new[]
            {
                SelectionInputCommand.CreatePoint(new Vector2(10f, 10f), 4f, SelectionApplyMode.Replace, expandSameClass: true),
            });
            globals[CoreServiceKeys.SelectionCandidatePolicy.Name] = new StubSelectionCandidatePolicy();
            globals[CoreServiceKeys.ScreenProjector.Name] = new StubScreenProjector();

            var system = new SelectionCommandSystem(world, globals);
            system.Update(0f);

            var buffer = world.Get<SelectionBuffer>(controller);
            Assert.That(buffer.Count, Is.EqualTo(2));
            Assert.That(buffer.Contains(first), Is.True);
            Assert.That(buffer.Contains(second), Is.True);
            Assert.That(buffer.Contains(third), Is.False);
        }

        private static Entity CreateSelectable(World world, int xCm, int yCm, int selectionClass)
        {
            return world.Create(
                WorldPositionCm.FromCm(xCm, yCm),
                new CullState { IsVisible = true },
                new TestSelectable { SelectionClass = selectionClass });
        }

        private sealed class StubSelectionInputHandler : ISelectionInputHandler
        {
            private readonly Queue<SelectionInputCommand> _commands;

            public StubSelectionInputHandler(IEnumerable<SelectionInputCommand> commands)
            {
                _commands = new Queue<SelectionInputCommand>(commands);
            }

            public void Update(float dt) { }

            public bool Poll(out SelectionInputCommand command)
            {
                if (_commands.Count > 0)
                {
                    command = _commands.Dequeue();
                    return true;
                }

                command = default;
                return false;
            }
        }

        private sealed class StubSelectionCandidatePolicy : ISelectionCandidatePolicy
        {
            public bool IsSelectable(World world, Entity controller, Entity candidate)
            {
                return world.Has<TestSelectable>(candidate);
            }

            public bool IsSameSelectionClass(World world, Entity reference, Entity candidate)
            {
                return world.Get<TestSelectable>(reference).SelectionClass == world.Get<TestSelectable>(candidate).SelectionClass;
            }
        }

        private sealed class StubScreenProjector : IScreenProjector
        {
            public Vector2 WorldToScreen(Vector3 worldPosition)
            {
                return new Vector2(worldPosition.X * 10f, worldPosition.Z * 10f);
            }
        }

        private struct TestSelectable
        {
            public int SelectionClass;
        }
    }
}
