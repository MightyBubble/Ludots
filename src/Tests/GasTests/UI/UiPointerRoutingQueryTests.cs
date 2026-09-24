using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Registry;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.UI
{
    /// <summary>
    /// UiPointerRoutingQuery (#1585 U1 groundwork): pointer routing reads entity state —
    /// the seat's rep active context chain — plus profile declarations, never view state.
    /// The newest chain entry that declares routing decides; undeclaring contexts defer to
    /// ancestors, mirroring the order drain's LIFO arbitration.
    /// </summary>
    [TestFixture]
    public sealed class UiPointerRoutingQueryTests
    {
        private const string UiProfileName = "interaction.context.ui.menu";
        private const string BattleProfileName = "interaction.context.tests.battle";

        private World _world = null!;
        private ClientLocalSeatRegistry _seats = null!;
        private InteractionContextProfileRegistry _profiles = null!;
        private StringIntRegistry _profileIds = null!;
        private Entity _rep;

        [SetUp]
        public void SetUp()
        {
            _world = World.Create();
            _rep = _world.Create();
            _seats = new ClientLocalSeatRegistry();
            _seats.Add(new ClientLocalSeat("seat.0") { PossessedPlayerId = 7, PossessedRep = _rep });

            _profileIds = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            _profiles = new InteractionContextProfileRegistry(_profileIds);
            _profiles.Install(new InteractionContextProfilesConfig
            {
                Profiles = new List<InteractionContextProfileDefinition>
                {
                    new() { Id = UiProfileName, PointerRouting = "ui" },
                    new() { Id = BattleProfileName },
                },
            },
            new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
            new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
            new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal));
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        [Test]
        public void MountedUiContext_RoutesPointerToUi()
        {
            MountSingle(UiProfileName);
            Assert.That(RoutesToUi(), Is.True);
        }

        [Test]
        public void MountedBattleContext_DoesNotRoute()
        {
            MountSingle(BattleProfileName);
            Assert.That(RoutesToUi(), Is.False);
        }

        [Test]
        public void Chain_NewestUndeclaringContext_DefersToAncestorUiDeclaration()
        {
            int uiId = _profileIds.GetId(UiProfileName);
            int battleId = _profileIds.GetId(BattleProfileName);
            var chain = new InteractionContextInstances();
            chain.Add(new InteractionContextInstance { ContextId = uiId });
            chain.Add(new InteractionContextInstance { ContextId = battleId });
            _world.Add(_rep, chain);

            Assert.That(RoutesToUi(), Is.True,
                "a derived context without a pointer declaration defers to the ancestor that declared one");
        }

        [Test]
        public void Chain_NewestDeclaringContext_Wins()
        {
            int uiId = _profileIds.GetId(UiProfileName);
            int battleId = _profileIds.GetId(BattleProfileName);
            var chain = new InteractionContextInstances();
            chain.Add(new InteractionContextInstance { ContextId = battleId });
            chain.Add(new InteractionContextInstance { ContextId = uiId });
            _world.Add(_rep, chain);

            Assert.That(RoutesToUi(), Is.True);
        }

        [Test]
        public void SeatWithoutPossession_DoesNotRoute()
        {
            _seats.ClearPossession("seat.0");
            MountSingle(UiProfileName);
            Assert.That(RoutesToUi(), Is.False);
        }

        [Test]
        public void UnknownSeat_FailsNamed()
        {
            MountSingle(UiProfileName);
            Assert.That(
                () => UiPointerRoutingQuery.RoutesPointerToUi(_world, _seats, _profiles, "seat.nope"),
                Throws.InvalidOperationException.With.Message.Contains("seat.nope"));
        }

        private void MountSingle(string profileName)
        {
            _world.Add(_rep, new InteractionContextInstance { ContextId = _profileIds.GetId(profileName) });
        }

        private bool RoutesToUi() =>
            UiPointerRoutingQuery.RoutesPointerToUi(_world, _seats, _profiles, "seat.0");
    }
}
