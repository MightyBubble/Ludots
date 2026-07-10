using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Scripting;
using ControlPlaneProjectionShowcaseMod.Runtime;

namespace ControlPlaneProjectionShowcaseMod.Systems
{
    /// <summary>
    /// M3 live demo of domain-routed writes (RFC-0065 DEC-4 / CTRL-4c): whenever P1Rep's command
    /// source changes, the source set is replayed through DomainRoutedCollectionWriter so each
    /// entity lands in its own control domain's (domainRep, CommandSource) collection.
    /// </summary>
    internal sealed class ControlPlaneRoutedSelectionSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly ControlPlaneProjectionScenarioState _state;
        private Entity[] _selectionScratch = new Entity[64];
        private uint _lastSelectionRevision;
        private bool _hasRoutedOnce;

        public ControlPlaneRoutedSelectionSystem(GameEngine engine, ControlPlaneProjectionScenarioState state)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            if (!_state.Ready)
            {
                return;
            }

            EntityCollectionStore? collections = _engine.GetService(CoreServiceKeys.EntityCollectionStore);
            DomainRoutedCollectionWriter? writer = _engine.GetService(CoreServiceKeys.DomainRoutedCollectionWriter);
            if (collections == null || writer == null)
            {
                return;
            }

            if (!collections.TryGetView(_state.P1Rep, EntityCollectionKeys.CommandSource, out EntityCollectionView view))
            {
                return;
            }

            if (_hasRoutedOnce && view.Revision == _lastSelectionRevision)
            {
                return;
            }

            EnsureScratchCapacity(view.Count);
            int count = collections.CopyEntities(_state.P1Rep, EntityCollectionKeys.CommandSource, _selectionScratch);
            // Every unit on this map is owned by a domain rep, so unresolved entities are a scenario bug.
            writer.ReplaceRouted(
                _state.P1Rep,
                _state.CommandSourceKeyId,
                _selectionScratch.AsSpan(0, count),
                EntityCollectionSourceKind.UiAcquisition,
                DomainRoutingUnresolvedPolicy.Reject);

            _lastSelectionRevision = view.Revision;
            _hasRoutedOnce = true;
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private void EnsureScratchCapacity(int required)
        {
            if (required <= _selectionScratch.Length)
            {
                return;
            }

            int next = _selectionScratch.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _selectionScratch, next);
        }
    }
}
