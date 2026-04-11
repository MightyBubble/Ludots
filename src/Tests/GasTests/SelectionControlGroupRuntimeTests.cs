using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Input.Selection;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class SelectionControlGroupRuntimeTests
    {
        [Test]
        public void SaveAndRecallGroup_MirrorsFormationAndRestoresLiveSelection()
        {
            using var world = World.Create();
            Entity viewer = world.Create();
            Entity first = world.Create();
            Entity second = world.Create();
            Entity replacement = world.Create();

            var selection = new SelectionRuntime(
                world,
                new SelectionRuntimeConfig(),
                new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: System.StringComparer.Ordinal));

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.SelectionRuntime.Name] = selection,
                [CoreServiceKeys.SelectionViewViewerEntity.Name] = viewer,
                [CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary,
            };

            Assert.That(selection.ReplaceSelection(viewer, SelectionSetKeys.LivePrimary, new[] { first, second }), Is.True);
            Assert.That(selection.TryBindView(viewer, SelectionViewKeys.Primary, viewer, SelectionSetKeys.LivePrimary), Is.True);

            Assert.That(
                SelectionControlGroupRuntime.TrySaveViewedSelectionToGroup(world, globals, selection, viewer, 1, mirrorToFormation: true),
                Is.True);
            Assert.That(selection.TryDescribeSelection(viewer, SelectionSetKeys.ControlGroup(1), out SelectionContainerDescriptor groupDescriptor), Is.True);
            Assert.That(groupDescriptor.MemberCount, Is.EqualTo(2));
            Assert.That(selection.TryDescribeSelection(viewer, SelectionSetKeys.FormationPrimary, out SelectionContainerDescriptor formationDescriptor), Is.True);
            Assert.That(formationDescriptor.MemberCount, Is.EqualTo(2));

            Assert.That(selection.ReplaceSelection(viewer, SelectionSetKeys.LivePrimary, new[] { replacement }), Is.True);
            Assert.That(selection.GetSelectionCount(viewer, SelectionSetKeys.LivePrimary), Is.EqualTo(1));

            Assert.That(
                SelectionControlGroupRuntime.TryRecallGroupToLive(world, globals, selection, viewer, 1, mirrorToFormation: true),
                Is.True);
            Assert.That(selection.GetSelectionCount(viewer, SelectionSetKeys.LivePrimary), Is.EqualTo(2));
            Assert.That(selection.TryGetSelectionAt(viewer, SelectionSetKeys.LivePrimary, 0, out Entity recalledPrimary), Is.True);
            Assert.That(recalledPrimary, Is.EqualTo(first));
            Assert.That(globals[CoreServiceKeys.SelectionViewKey.Name], Is.EqualTo(SelectionViewKeys.Primary));
            Assert.That(selection.TryDescribeSelection(viewer, SelectionSetKeys.FormationPrimary, out formationDescriptor), Is.True);
            Assert.That(formationDescriptor.MemberCount, Is.EqualTo(2));
        }

        [Test]
        public void SelectionRuntime_RejectsBlankAliasAndViewKeys()
        {
            using var world = World.Create();
            Entity viewer = world.Create();
            Entity target = world.Create();

            var selection = new SelectionRuntime(
                world,
                new SelectionRuntimeConfig(),
                new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: System.StringComparer.Ordinal));

            Assert.That(selection.TryGetSelectionEntity(viewer, string.Empty, out _), Is.False);
            Assert.That(selection.TryGetOrCreateSelectionEntity(viewer, string.Empty, out _), Is.False);
            Assert.That(selection.ReplaceSelection(viewer, SelectionSetKeys.LivePrimary, new[] { target }), Is.True);
            Assert.That(selection.TryBindView(viewer, string.Empty, viewer, SelectionSetKeys.LivePrimary), Is.False);
            Assert.That(selection.TryBindView(viewer, SelectionViewKeys.Primary, viewer, string.Empty), Is.False);
            Assert.That(selection.TryResolveViewContainer(viewer, string.Empty, out _), Is.False);
        }

        [Test]
        public void SelectionViewRuntime_RequiresExplicitViewerViewKeyAndBinding()
        {
            using var world = World.Create();
            Entity viewer = world.Create();
            Entity target = world.Create();

            var selection = new SelectionRuntime(
                world,
                new SelectionRuntimeConfig(),
                new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: System.StringComparer.Ordinal));
            Assert.That(selection.ReplaceSelection(viewer, SelectionSetKeys.LivePrimary, new[] { target }), Is.True);

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.SelectionRuntime.Name] = selection,
                [CoreServiceKeys.LocalPlayerEntity.Name] = viewer,
            };

            Assert.That(SelectionViewRuntime.TryResolveViewedSelection(world, globals, selection, out _, out _, out _), Is.False);

            globals[CoreServiceKeys.SelectionViewViewerEntity.Name] = viewer;
            Assert.That(SelectionViewRuntime.TryResolveViewedSelection(world, globals, selection, out _, out _, out _), Is.False);

            globals[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary;
            Assert.That(SelectionViewRuntime.TryResolveViewedSelection(world, globals, selection, out _, out _, out _), Is.False);

            Assert.That(selection.TryBindView(viewer, SelectionViewKeys.Primary, viewer, SelectionSetKeys.LivePrimary), Is.True);
            Assert.That(SelectionViewRuntime.TryResolveViewedSelection(world, globals, selection, out Entity resolvedViewer, out string resolvedViewKey, out Entity container), Is.True);
            Assert.That(resolvedViewer, Is.EqualTo(viewer));
            Assert.That(resolvedViewKey, Is.EqualTo(SelectionViewKeys.Primary));
            Assert.That(selection.TryGetPrimary(container, out Entity primary), Is.True);
            Assert.That(primary, Is.EqualTo(target));
        }
    }
}
