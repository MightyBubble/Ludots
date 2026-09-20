using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Knowledge
{
    public readonly record struct LocalDisclosureChangeStamp(int StructuralRevision, int DefinitionRegistryVersion);

    /// <summary>
    /// Target description for <see cref="LocalObserverLiveDisclosureSystem"/>: which entities are
    /// disclosed, when the disclosure is active, and which attribute mask the observer receives.
    /// Implementations stay stateless; the disclosure system owns idempotency.
    /// </summary>
    public interface ILocalObserverDisclosureTarget
    {
        bool IsActivated(GameEngine engine);

        /// <summary>Expected target count for this frame; non-positive means not yet applicable.</summary>
        int ResolveExpectedTargetCount(GameEngine engine);

        /// <summary>Currently spawned target count; publishing waits until it reaches the expectation.</summary>
        int ResolveObservedTargetCount(GameEngine engine);

        KnowledgeIdMask256 ResolveAttributeMask(GameEngine engine);

        LocalDisclosureChangeStamp ResolveChangeStamp(GameEngine engine);
    }

    /// <summary>
    /// Publishes LiveVisible knowledge for every target-matching entity to the local observer, so
    /// presenters, minimap markers, and command-source selection work without per-showcase
    /// disclosure systems. Republishes only when the viewer, target structure, definition
    /// registry, or attribute mask changes.
    /// </summary>
    public sealed class LocalObserverLiveDisclosureSystem : BaseSystem<World, float>
    {
        private const int LiveKnowledgeConfidencePermille = 1000;

        private readonly GameEngine _engine;
        private readonly ILocalObserverDisclosureTarget _target;
        private readonly QueryDescription _targetQuery;
        private Entity _publishedViewer = Entity.Null;
        private LocalDisclosureChangeStamp _publishedStamp;
        private KnowledgeIdMask256 _publishedAttributeMask;

        public LocalObserverLiveDisclosureSystem(
            GameEngine engine,
            QueryDescription targetQuery,
            ILocalObserverDisclosureTarget target)
            : base(engine?.World ?? throw new ArgumentNullException(nameof(engine)))
        {
            _engine = engine;
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _targetQuery = targetQuery;
        }

        public override void Update(in float dt)
        {
            if (!_target.IsActivated(_engine) ||
                _engine.GetService(CoreServiceKeys.KnowledgeProjectionStore) is not KnowledgeProjectionStore knowledge ||
                !TryResolveViewer(out Entity viewer))
            {
                return;
            }

            int expectedTargetCount = _target.ResolveExpectedTargetCount(_engine);
            if (expectedTargetCount <= 0 || _target.ResolveObservedTargetCount(_engine) < expectedTargetCount)
            {
                return;
            }

            KnowledgeIdMask256 attributeMask = _target.ResolveAttributeMask(_engine);
            LocalDisclosureChangeStamp stamp = _target.ResolveChangeStamp(_engine);
            if (_publishedViewer == viewer &&
                _publishedStamp == stamp &&
                _publishedAttributeMask == attributeMask)
            {
                return;
            }

            int observedTick = KnowledgeProjectionConsumer.ResolveCurrentTick(_engine.GlobalContext);
            var emptyMask = KnowledgeIdMask256.Empty;
            var record = new KnowledgeDisclosureRecord(
                KnowledgePresence.LiveVisible,
                KnowledgePositionAccess.Live,
                attributeMask,
                emptyMask,
                emptyMask,
                viewer,
                observedTick,
                expiryTick: 0,
                confidencePermille: LiveKnowledgeConfidencePermille,
                revision: 0);

            int published = PublishTargetKnowledge(knowledge, viewer, in record);
            if (published < expectedTargetCount)
            {
                return;
            }

            _publishedViewer = viewer;
            _publishedStamp = stamp;
            _publishedAttributeMask = attributeMask;
        }

        private bool TryResolveViewer(out Entity viewer)
        {
            Entity candidate = ClientLocalSeatAccess.RequireSolePossessedRep(_engine);
            viewer = candidate;
            return candidate != Entity.Null && World.IsAlive(candidate);
        }

        private int PublishTargetKnowledge(
            KnowledgeProjectionStore knowledge,
            Entity viewer,
            in KnowledgeDisclosureRecord record)
        {
            int published = 0;
            foreach (ref var chunk in World.Query(in _targetQuery))
            {
                ref Entity firstEntity = ref chunk.Entity(0);
                foreach (int index in chunk)
                {
                    Entity target = Unsafe.Add(ref firstEntity, index);
                    knowledge.Upsert(viewer, target, in record);
                    published++;
                }
            }

            return published;
        }
    }
}
