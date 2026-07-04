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
                new SelectionRuntimeConfig
                {
                    TargetFilter = new SelectionTargetFilterConfig { RelationFilter = "All" },
                },
                new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: System.StringComparer.Ordinal));

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.SelectionRuntime.Name] = selection,
                [CoreServiceKeys.EntityViewViewerEntity.Name] = viewer,
                [CoreServiceKeys.EntityViewKey.Name] = SelectionViewKeys.Primary,
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
            Assert.That(globals[CoreServiceKeys.EntityViewKey.Name], Is.EqualTo(SelectionViewKeys.Primary));
            Assert.That(selection.TryDescribeSelection(viewer, SelectionSetKeys.FormationPrimary, out formationDescriptor), Is.True);
            Assert.That(formationDescriptor.MemberCount, Is.EqualTo(2));
        }
    }
}
