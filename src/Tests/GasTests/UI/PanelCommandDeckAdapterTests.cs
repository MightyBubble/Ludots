using System;
using System.Collections.Generic;
using Ludots.Core.UI.CommandDeck;
using Ludots.Core.UI.PanelProjection;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.UI
{
    /// <summary>
    /// CommandDeck → panel data adapter (#1609): one snapshot, one vocabulary — pins for
    /// the frame facts, one item per slot with identity/state/route fields the Button
    /// payload chain bakes per slot.
    /// </summary>
    [TestFixture]
    public sealed class PanelCommandDeckAdapterTests
    {
        [Test]
        public void Snapshot_ProjectsFramePinsAndSlotItems()
        {
            CommandDeckSnapshot snapshot = Snapshot(
                revision: 77u,
                visible: true,
                Entry("attack", 0, "Attack", label: "攻击", lockoutPermille: 500, charges: 2, max: 3, blocked: null),
                Entry("recall", 1, "Recall", label: "回城", lockoutPermille: 0, charges: 0, max: 0, blocked: "冷却中"));

            Dictionary<string, float> values = PanelCommandDeckAdapter.ToValues(snapshot);
            Assert.That(values[PanelCommandDeckAdapter.RevisionPin], Is.EqualTo(77f));
            Assert.That(values[PanelCommandDeckAdapter.EntryCountPin], Is.EqualTo(2f));
            Assert.That(values[PanelCommandDeckAdapter.VisiblePin], Is.EqualTo(1f));

            PanelListProjection list = PanelCommandDeckAdapter.ToListProjection(snapshot);
            Assert.That(list.Name, Is.EqualTo(PanelCommandDeckAdapter.ListName));
            Assert.That(list.Items.Count, Is.EqualTo(2));
            Assert.That(list.Items[0].Strings["actionId"], Is.EqualTo("attack"));
            Assert.That(list.Items[0].Strings["label"], Is.EqualTo("攻击"));
            Assert.That(list.Items[0].Floats["lockout"], Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(list.Items[0].Floats["chargeRatio"], Is.EqualTo(2f / 3f).Within(0.0001f));
            Assert.That(list.Items[0].Bools["blocked"], Is.False);
            Assert.That(list.Items[1].Bools["blocked"], Is.True);
            Assert.That(list.Items[1].Strings["blockedReason"], Is.EqualTo("冷却中"));
            Assert.That(list.Items[1].Strings["slotKey"], Is.EqualTo("1"));
        }

        [Test]
        public void HiddenSnapshot_ZeroEntriesAndInvisiblePin()
        {
            CommandDeckSnapshot snapshot = new("deck.profile", CommandDeckDisplayMode.Global, revision: 3u, visible: false, Array.Empty<CommandDeckEntry>());

            Dictionary<string, float> values = PanelCommandDeckAdapter.ToValues(snapshot);
            Assert.That(values[PanelCommandDeckAdapter.VisiblePin], Is.EqualTo(0f));
            Assert.That(values[PanelCommandDeckAdapter.EntryCountPin], Is.EqualTo(0f));
            Assert.That(PanelCommandDeckAdapter.ToListProjection(snapshot).Items.Count, Is.Zero);
        }

        private static CommandDeckEntry Entry(
            string actionId,
            int slotIndex,
            string categoryId,
            string label,
            short lockoutPermille,
            short charges,
            short max,
            string? blocked) => new(
                slotIndex,
                abilityId: 10 + slotIndex,
                actionId,
                label,
                categoryId,
                status: "ready",
                blockedReason: blocked ?? string.Empty,
                ownerCount: 1,
                routeProfileId: "route.default",
                routedOwnerEntityId: 0,
                routedOwnerVersion: 0,
                routedSlotIndex: slotIndex,
                lockoutPermille,
                charges,
                max);

        private static CommandDeckSnapshot Snapshot(
            uint revision,
            bool visible,
            params CommandDeckEntry[] entries) => new(
                "deck.profile",
                CommandDeckDisplayMode.Global,
                revision,
                visible,
                entries);
    }
}
