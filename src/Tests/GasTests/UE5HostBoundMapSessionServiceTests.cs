using System.Collections.Generic;
using Ludots.Adapter.UE5;
using Ludots.Core.Config;
using Ludots.Core.Map;
using NUnit.Framework;

namespace GasTests
{
    [TestFixture]
    public class UE5HostBoundMapSessionServiceTests
    {
        [Test]
        public void Reconcile_WithoutExplicitBinding_DoesNotEnterHostBoundPath()
        {
            HostBoundMapSessionSnapshot published = HostBoundMapSessionSnapshot.Empty;
            var service = new UE5HostBoundMapSessionService(
                resolverAccessor: () => null,
                navigatorAccessor: () => new StubNavigator(new HostLevelNavigationSnapshot(
                    HostLevelTransitionMode.DirectOpenLevel,
                    HostLevelNavigationState.Active,
                    "/Game/Maps/Menu",
                    "/Game/Maps/Menu",
                    "MenuWorld",
                    string.Empty)),
                publishSnapshot: snapshot => published = snapshot);

            var snapshot = service.Reconcile(CreateSession("entry_map"));

            Assert.That(snapshot.FocusedMapId, Is.EqualTo("entry_map"));
            Assert.That(snapshot.HasExplicitBinding, Is.False);
            Assert.That(snapshot.IsHostReady, Is.False);
            Assert.That(snapshot.HasPendingReturn, Is.False);
            Assert.That(snapshot.Navigation, Is.EqualTo(HostLevelNavigationSnapshot.Empty));
            Assert.That(published, Is.EqualTo(snapshot));
        }

        [Test]
        public void Reconcile_WithExplicitBinding_KeepsMapLoadedDistinctFromHostReady()
        {
            HostBoundMapSessionSnapshot published = HostBoundMapSessionSnapshot.Empty;
            var resolver = new StubResolver(CreateBinding());
            var navigator = new StubNavigator(new HostLevelNavigationSnapshot(
                HostLevelTransitionMode.DirectOpenLevel,
                HostLevelNavigationState.Active,
                "/Game/Maps/Battle",
                "/Game/Maps/Battle",
                "BattleWorld",
                string.Empty));
            var service = new UE5HostBoundMapSessionService(
                resolverAccessor: () => resolver,
                navigatorAccessor: () => navigator,
                publishSnapshot: snapshot => published = snapshot);

            HostBoundMapSessionSnapshot reconciled = service.Reconcile(CreateSession("battle_map"));

            Assert.That(reconciled.HasExplicitBinding, Is.True);
            Assert.That(reconciled.IsHostReady, Is.False, "Map focus alone must not imply host-ready.");
            Assert.That(reconciled.HasPendingReturn, Is.False);
            Assert.That(reconciled.Navigation.CurrentWorldName, Is.EqualTo("BattleWorld"));

            HostBoundMapSessionSnapshot ready = service.SetHostReady(true);

            Assert.That(ready.IsHostReady, Is.True);
            Assert.That(ready.HasExplicitBinding, Is.True);
            Assert.That(ready.Navigation.CurrentWorldName, Is.EqualTo("BattleWorld"));
            Assert.That(published, Is.EqualTo(ready));
        }

        [Test]
        public void Reconcile_EquivalentBindingContent_PreservesOwnedState()
        {
            var resolver = new CyclingResolver(
                CreateBinding(
                    streamingLevels: new[] { "Geo", "Lighting" },
                    metadata: new Dictionary<string, string> { ["profile"] = "battle" }),
                CreateBinding(
                    streamingLevels: new[] { "Geo", "Lighting" },
                    metadata: new Dictionary<string, string> { ["profile"] = "battle" }));
            var service = new UE5HostBoundMapSessionService(
                resolverAccessor: () => resolver,
                navigatorAccessor: () => new StubNavigator(HostLevelNavigationSnapshot.Empty),
                publishSnapshot: _ => { });
            var session = CreateSession("battle_map");

            service.Reconcile(session);
            service.SetHostReady(true);
            service.SetPendingReturn(true);

            HostBoundMapSessionSnapshot snapshot = service.Reconcile(session);

            Assert.That(snapshot.IsHostReady, Is.True);
            Assert.That(snapshot.HasPendingReturn, Is.True);
        }

        [Test]
        public void Reconcile_WhenFocusedMapBindingChanges_ResetsOwnedState()
        {
            var resolver = new CyclingResolver(CreateBinding(levelPath: "/Game/Maps/A"), CreateBinding(levelPath: "/Game/Maps/B"));
            var service = new UE5HostBoundMapSessionService(
                resolverAccessor: () => resolver,
                navigatorAccessor: () => new StubNavigator(HostLevelNavigationSnapshot.Empty),
                publishSnapshot: _ => { });
            var session = CreateSession("battle_map");

            service.Reconcile(session);
            service.SetHostReady(true);
            service.SetPendingReturn(true);

            HostBoundMapSessionSnapshot snapshot = service.Reconcile(session);

            Assert.That(snapshot.HasExplicitBinding, Is.True);
            Assert.That(snapshot.IsHostReady, Is.False);
            Assert.That(snapshot.HasPendingReturn, Is.False);
            Assert.That(snapshot.Binding.LevelPath, Is.EqualTo("/Game/Maps/B"));
        }

        private static MapSession CreateSession(string mapId)
        {
            return new MapSession(new MapId(mapId), new MapConfig { Id = mapId });
        }

        private static ExplicitHostMapBinding CreateBinding(
            string hostWorldName = "BattleWorld",
            string levelPath = "/Game/Maps/Battle",
            IReadOnlyList<string>? streamingLevels = null,
            IReadOnlyDictionary<string, string>? metadata = null)
        {
            return new ExplicitHostMapBinding(
                hostWorldName,
                levelPath,
                HostLevelTransitionMode.DirectOpenLevel,
                streamingLevels != null,
                streamingLevels,
                metadata);
        }

        private sealed class StubResolver : IExplicitHostMapBindingResolver
        {
            private readonly ExplicitHostMapBinding _binding;

            public StubResolver(ExplicitHostMapBinding binding)
            {
                _binding = binding;
            }

            public bool TryResolve(MapSession focusedSession, out ExplicitHostMapBinding binding)
            {
                binding = _binding;
                return true;
            }
        }

        private sealed class CyclingResolver : IExplicitHostMapBindingResolver
        {
            private readonly Queue<ExplicitHostMapBinding> _bindings;

            public CyclingResolver(params ExplicitHostMapBinding[] bindings)
            {
                _bindings = new Queue<ExplicitHostMapBinding>(bindings);
            }

            public bool TryResolve(MapSession focusedSession, out ExplicitHostMapBinding binding)
            {
                binding = _bindings.Count > 1 ? _bindings.Dequeue() : _bindings.Peek();
                return true;
            }
        }

        private sealed class StubNavigator : IHostLevelNavigator
        {
            public StubNavigator(HostLevelNavigationSnapshot snapshot)
            {
                Snapshot = snapshot;
            }

            public HostLevelNavigationSnapshot Snapshot { get; }

            public HostLevelNavigationResult Load(in HostLevelLoadRequest request)
            {
                return HostLevelNavigationResult.Ok(Snapshot);
            }

            public HostLevelNavigationResult ExitPreview()
            {
                return HostLevelNavigationResult.Ok(Snapshot);
            }
        }
    }
}
