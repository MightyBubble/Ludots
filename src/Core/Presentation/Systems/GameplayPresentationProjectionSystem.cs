using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Presentation.Events;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class GameplayPresentationProjectionSystem : BaseSystem<World, float>
    {
        private readonly GameplayEventBus _eventBus;
        private readonly GasPresentationEventBuffer _gasEvents;
        private readonly PresentationEventStream _stream;
        private readonly PresentationOwnerChangeBuffer _ownerChanges;
        private readonly GameSession _session;

        private readonly QueryDescription _tagChangedQuery = new QueryDescription()
            .WithAll<GameplayTagEffectiveChangedBits, GameplayTagEffectiveCache>();
        private readonly QueryDescription _attributeChangedQuery = new QueryDescription()
            .WithAll<GameplayAttributeChangedBits, AttributeBuffer>();

        public GameplayPresentationProjectionSystem(
            World world,
            GameplayEventBus eventBus,
            PresentationEventStream stream,
            GameSession session,
            GasPresentationEventBuffer gasEvents,
            PresentationOwnerChangeBuffer ownerChanges) : base(world)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _gasEvents = gasEvents ?? throw new ArgumentNullException(nameof(gasEvents));
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _ownerChanges = ownerChanges ?? throw new ArgumentNullException(nameof(ownerChanges));
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public override void Update(in float dt)
        {
            int tick = _session.CurrentTick;

            // Project gameplay events into the presentation event stream for performer rules.
            var events = _eventBus.Events;
            for (int i = 0; i < events.Count; i++)
            {
                var evt = events[i];
                AddEventOrThrow(
                    _stream,
                    new PresentationEvent
                    {
                        LogicTickStamp = tick,
                        Kind = PresentationEventKind.GameplayEvent,
                        KeyId = evt.TagId,
                        Source = evt.Source,
                        Target = evt.Target,
                        Magnitude = evt.Magnitude
                    },
                    nameof(GameplayEventBus));
            }

            // Project GAS-authored presentation events into the shared presentation stream.
            if (_gasEvents.Count > 0)
            {
                var gasSpan = _gasEvents.Events;
                for (int i = 0; i < gasSpan.Length; i++)
                {
                    ref readonly var ge = ref gasSpan[i];
                    switch (ge.Kind)
                    {
                        case GasPresentationEventKind.EffectApplied:
                            AddEventOrThrow(
                                _stream,
                                new PresentationEvent
                                {
                                    LogicTickStamp = tick,
                                    Kind = PresentationEventKind.EffectApplied,
                                    KeyId = ge.EffectTemplateId,
                                    Source = ge.Target,
                                    Target = ge.Actor,
                                    Magnitude = ge.Delta,
                                    PayloadA = ge.AttributeId,
                                    PayloadB = ge.AbilitySlot,
                                },
                                nameof(GasPresentationEventKind.EffectApplied));
                            break;
                        case GasPresentationEventKind.EffectActivated:
                            AddEventOrThrow(
                                _stream,
                                new PresentationEvent
                                {
                                    LogicTickStamp = tick,
                                    Kind = PresentationEventKind.EffectActivated,
                                    KeyId = ge.EffectTemplateId,
                                    Source = ge.Target,
                                    Target = ge.Actor,
                                    Magnitude = ge.Delta,
                                    PayloadA = ge.AttributeId,
                                    PayloadB = ge.AbilitySlot,
                                },
                                nameof(GasPresentationEventKind.EffectActivated));
                            break;
                        case GasPresentationEventKind.EffectExpired:
                            AddEventOrThrow(
                                _stream,
                                new PresentationEvent
                                {
                                    LogicTickStamp = tick,
                                    Kind = PresentationEventKind.EffectExpired,
                                    KeyId = ge.EffectTemplateId,
                                    Source = ge.Actor,
                                    Target = ge.Target,
                                    Magnitude = 0f,
                                    PayloadA = ge.EffectTemplateId,
                                },
                                nameof(GasPresentationEventKind.EffectExpired));
                            break;
                        case GasPresentationEventKind.EffectCancelled:
                            AddEventOrThrow(
                                _stream,
                                new PresentationEvent
                                {
                                    LogicTickStamp = tick,
                                    Kind = PresentationEventKind.EffectCancelled,
                                    KeyId = ge.EffectTemplateId,
                                    Source = ge.Actor,
                                    Target = ge.Target,
                                    Magnitude = 0f,
                                    PayloadA = ge.EffectTemplateId,
                                },
                                nameof(GasPresentationEventKind.EffectCancelled));
                            break;
                        case GasPresentationEventKind.CastStarted:
                            AddEventOrThrow(
                                _stream,
                                new PresentationEvent
                                {
                                    LogicTickStamp = tick,
                                    Kind = PresentationEventKind.CastStarted,
                                    KeyId = ge.AbilityId,
                                    Source = ge.Actor,
                                    Target = ge.Target,
                                    Magnitude = 0f,
                                    PayloadA = ge.AbilitySlot,
                                    PayloadB = ge.AbilityId,
                                },
                                nameof(GasPresentationEventKind.CastStarted));
                            break;
                        case GasPresentationEventKind.CastCommitted:
                            AddEventOrThrow(
                                _stream,
                                new PresentationEvent
                                {
                                    LogicTickStamp = tick,
                                    Kind = PresentationEventKind.CastCommitted,
                                    KeyId = ge.AbilityId,
                                    Source = ge.Actor,
                                    Target = ge.Target,
                                    Magnitude = 0f,
                                    PayloadA = ge.AbilitySlot,
                                    PayloadB = ge.AbilityId,
                                },
                                nameof(GasPresentationEventKind.CastCommitted));
                            break;
                        case GasPresentationEventKind.CastFailed:
                            AddEventOrThrow(
                                _stream,
                                new PresentationEvent
                                {
                                    LogicTickStamp = tick,
                                    Kind = PresentationEventKind.CastFailed,
                                    KeyId = ge.AbilityId,
                                    Source = ge.Actor,
                                    Target = ge.Target,
                                    Magnitude = 0f,
                                    PayloadA = ge.AbilitySlot,
                                    PayloadB = (int)ge.FailReason,
                                },
                                nameof(GasPresentationEventKind.CastFailed));
                            break;
                        case GasPresentationEventKind.CastFinished:
                            AddEventOrThrow(
                                _stream,
                                new PresentationEvent
                                {
                                    LogicTickStamp = tick,
                                    Kind = PresentationEventKind.CastFinished,
                                    KeyId = ge.AbilityId,
                                    Source = ge.Actor,
                                    Target = ge.Target,
                                    Magnitude = 0f,
                                    PayloadA = ge.AbilitySlot,
                                    PayloadB = ge.AbilityId,
                                },
                                nameof(GasPresentationEventKind.CastFinished));
                            break;
                        case GasPresentationEventKind.CastInterrupted:
                            AddEventOrThrow(
                                _stream,
                                new PresentationEvent
                                {
                                    LogicTickStamp = tick,
                                    Kind = PresentationEventKind.CastInterrupted,
                                    KeyId = ge.AbilityId,
                                    Source = ge.Actor,
                                    Target = ge.Target,
                                    Magnitude = 0f,
                                    PayloadA = ge.AbilitySlot,
                                    PayloadB = ge.AbilityId,
                                },
                                nameof(GasPresentationEventKind.CastInterrupted));
                            break;
                    }
                }

                _gasEvents.Clear();
            }

            // Project owner fact changes into both the event stream and the owner-change index.
            var job = new TagChangedJob
            {
                Stream = _stream,
                OwnerChanges = _ownerChanges,
                Tick = tick
            };
            World.InlineEntityQuery<TagChangedJob, GameplayTagEffectiveChangedBits, GameplayTagEffectiveCache>(in _tagChangedQuery, ref job);

            var attributeJob = new AttributeChangedJob
            {
                Stream = _stream,
                OwnerChanges = _ownerChanges,
                Tick = tick
            };
            World.InlineEntityQuery<AttributeChangedJob, GameplayAttributeChangedBits, AttributeBuffer>(in _attributeChangedQuery, ref attributeJob);
        }

        private struct TagChangedJob : IForEachWithEntity<GameplayTagEffectiveChangedBits, GameplayTagEffectiveCache>
        {
            public PresentationEventStream Stream;
            public PresentationOwnerChangeBuffer OwnerChanges;
            public int Tick;

            public unsafe void Update(Entity entity, ref GameplayTagEffectiveChangedBits changed, ref GameplayTagEffectiveCache cache)
            {
                fixed (ulong* words = changed.Bits)
                {
                    for (int wordIndex = 0; wordIndex < 4; wordIndex++)
                    {
                        ulong bits = words[wordIndex];
                        while (bits != 0)
                        {
                            int bit = BitOperations.TrailingZeroCount(bits);
                            bits &= bits - 1;
                            int tagId = (wordIndex << 6) + bit;
                            bool now = cache.Has(tagId);

                            AddEventOrThrow(
                                Stream,
                                new PresentationEvent
                                {
                                    LogicTickStamp = Tick,
                                    Kind = PresentationEventKind.TagEffectiveChanged,
                                    KeyId = tagId,
                                    Source = entity,
                                    Target = entity,
                                    Magnitude = now ? 1f : 0f
                                },
                                nameof(TagChangedJob));
                            if (!OwnerChanges.TryAdd(new PresentationOwnerChange(entity, PresentationOwnerChangeKind.Tag, tagId, now ? (byte)1 : (byte)0)))
                            {
                                throw new InvalidOperationException(
                                    $"PresentationOwnerChangeBuffer overflow while recording tag change tagId={tagId}.");
                            }
                        }
                    }
                }
            }
        }

        private struct AttributeChangedJob : IForEachWithEntity<GameplayAttributeChangedBits, AttributeBuffer>
        {
            public PresentationEventStream Stream;
            public PresentationOwnerChangeBuffer OwnerChanges;
            public int Tick;

            public unsafe void Update(Entity entity, ref GameplayAttributeChangedBits changed, ref AttributeBuffer attributes)
            {
                fixed (byte* bits = changed.Bits)
                {
                    for (int attributeId = 0; attributeId < AttributeBuffer.MAX_ATTRS; attributeId++)
                    {
                        if (bits[attributeId] == 0)
                        {
                            continue;
                        }

                        AddEventOrThrow(
                            Stream,
                            new PresentationEvent
                            {
                                LogicTickStamp = Tick,
                                Kind = PresentationEventKind.AttributeValueChanged,
                                KeyId = attributeId,
                                Source = entity,
                                Target = entity,
                                Magnitude = attributes.GetCurrent(attributeId),
                            },
                            nameof(AttributeChangedJob));
                        if (!OwnerChanges.TryAdd(new PresentationOwnerChange(entity, PresentationOwnerChangeKind.Attribute, attributeId)))
                        {
                            throw new InvalidOperationException(
                                $"PresentationOwnerChangeBuffer overflow while recording attribute change attributeId={attributeId}.");
                        }
                    }
                }
            }
        }

        private static void AddEventOrThrow(
            PresentationEventStream stream,
            PresentationEvent evt,
            string source)
        {
            if (!stream.TryAdd(evt))
            {
                throw new InvalidOperationException(
                    $"PresentationEventStream overflow while projecting {source} event kind={evt.Kind}, keyId={evt.KeyId}, capacity={stream.Capacity}.");
            }
        }
    }
}
