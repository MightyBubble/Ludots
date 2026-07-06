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
    /// Recomputes the viewer-relative marker projection whenever the composite control plane view
    /// changes: ControlPlaneView.CopyMembersWithDomain(P1Rep, CommandSource) rows are partitioned by
    /// row domain into two presentation projection collections (owned vs proxied, owner = P1Rep).
    /// This is the mod-level projection layer that stands in until DEC-5's PROV-4b graph conditions
    /// land; collection.ui.* projections never write back into domain collections. Scratch arrays are
    /// reused so the steady state allocates nothing.
    /// </summary>
    internal sealed class ControlPlaneMarkerProjectionSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly ControlPlaneProjectionScenarioState _state;
        private EntityCollectionDescriptor _ownedDescriptor;
        private EntityCollectionDescriptor _proxiedDescriptor;
        private bool _descriptorsReady;
        private Entity[] _memberScratch = new Entity[64];
        private Entity[] _domainScratch = new Entity[64];
        private Entity[] _ownedScratch = new Entity[64];
        private Entity[] _proxiedScratch = new Entity[64];
        private uint _lastViewRevision;
        private bool _hasProjectedOnce;

        public ControlPlaneMarkerProjectionSystem(GameEngine engine, ControlPlaneProjectionScenarioState state)
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

            ControlPlaneView? view = _engine.GetService(CoreServiceKeys.ControlPlaneView);
            EntityCollectionStore? store = _engine.GetService(CoreServiceKeys.EntityCollectionStore);
            if (view == null || store == null)
            {
                return;
            }

            uint revision = view.ComputeRevision(_state.P1Rep, _state.CommandSourceKeyId);
            if (_hasProjectedOnce && revision == _lastViewRevision)
            {
                return;
            }

            int count = CopyView(view);
            int ownedCount = 0;
            int proxiedCount = 0;
            for (int i = 0; i < count; i++)
            {
                if (_domainScratch[i] == _state.P1Rep)
                {
                    _ownedScratch[ownedCount++] = _memberScratch[i];
                }
                else
                {
                    _proxiedScratch[proxiedCount++] = _memberScratch[i];
                }
            }

            EnsureDescriptors();
            store.Replace(_state.P1Rep, in _ownedDescriptor, _ownedScratch.AsSpan(0, ownedCount), _state.P1Rep);
            store.Replace(_state.P1Rep, in _proxiedDescriptor, _proxiedScratch.AsSpan(0, proxiedCount), _state.P1Rep);

            _lastViewRevision = revision;
            _hasProjectedOnce = true;
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private int CopyView(ControlPlaneView view)
        {
            while (true)
            {
                int count = view.CopyMembersWithDomain(
                    _state.P1Rep,
                    _state.CommandSourceKeyId,
                    _memberScratch,
                    _domainScratch);
                if (count < _memberScratch.Length)
                {
                    return count;
                }

                // Buffer may have truncated the copy; grow and retry.
                int next = _memberScratch.Length * 2;
                Array.Resize(ref _memberScratch, next);
                Array.Resize(ref _domainScratch, next);
                Array.Resize(ref _ownedScratch, next);
                Array.Resize(ref _proxiedScratch, next);
            }
        }

        private void EnsureDescriptors()
        {
            if (_descriptorsReady)
            {
                return;
            }

            _ownedDescriptor = EntityCollectionDescriptor.Create(
                ControlPlaneProjectionShowcaseIds.OwnedProjectionCollectionKey,
                EntityCollectionSourceKind.RelationDerived,
                EntityCollectionRoleKind.Display,
                title: "Control plane owned projection",
                summary: "Command-source members maintained inside P1Rep's own domain.");
            _proxiedDescriptor = EntityCollectionDescriptor.Create(
                ControlPlaneProjectionShowcaseIds.ProxiedProjectionCollectionKey,
                EntityCollectionSourceKind.RelationDerived,
                EntityCollectionRoleKind.Display,
                title: "Control plane proxied projection",
                summary: "Command-source members reached only through Controls grants.");
            _descriptorsReady = true;
        }
    }
}
