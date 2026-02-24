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
    public sealed class PresentationBridgeSystem : BaseSystem<World, float>
    {
        private readonly GameplayEventBus _eventBus;
        private readonly GasPresentationEventBuffer _gasEvents;
        private readonly PresentationEventStream _stream;
        private readonly GameSession _session;

        private readonly QueryDescription _tagChangedQuery = new QueryDescription()
            .WithAll<GameplayTagEffectiveChangedBits, GameplayTagEffectiveCache>();

        public PresentationBridgeSystem(World world, GameplayEventBus eventBus, PresentationEventStream stream, GameSession session, GasPresentationEventBuffer gasEvents = null) : base(world)
        {
            _eventBus = eventBus;
            _gasEvents = gasEvents;
            _stream = stream;
            _session = session;
        }

        public override void Update(in float dt)
        {
            int tick = _session.CurrentTick;

            // Bridge GameplayEventBus → PresentationEventStream
            var events = _eventBus.Events;
            for (int i = 0; i < events.Count; i++)
            {
                var evt = events[i];
                _stream.TryAdd(new PresentationEvent
                {
                    LogicTickStamp = tick,
                    Kind = PresentationEventKind.GameplayEvent,
                    KeyId = evt.TagId,
                    Source = evt.Source,
                    Target = evt.Target,
                    Magnitude = evt.Magnitude
                });
            }

            // Bridge GasPresentationEventBuffer → PresentationEventStream
            if (_gasEvents != null)
            {
                var gasSpan = _gasEvents.Events;
                for (int i = 0; i < gasSpan.Length; i++)
                {
                    ref readonly var ge = ref gasSpan[i];
                    switch (ge.Kind)
                    {
                        case GasPresentationEventKind.EffectApplied:
                            _stream.TryAdd(new PresentationEvent
                            {
                                LogicTickStamp = tick,
                                Kind = PresentationEventKind.EffectApplied,
                                KeyId = ge.EffectTemplateId,
                                Source = ge.Actor,
                                Target = ge.Target,
                                Magnitude = ge.Delta,
                                PayloadA = ge.AttributeId,
                                PayloadB = ge.AbilitySlot,
                            });
                            break;
                        case GasPresentationEventKind.CastCommitted:
                            _stream.TryAdd(new PresentationEvent
                            {
                                LogicTickStamp = tick,
                                Kind = PresentationEventKind.CastCommitted,
                                KeyId = ge.AbilityId,
                                Source = ge.Actor,
                                Target = ge.Target,
                                Magnitude = 0f,
                                PayloadA = ge.AbilitySlot,
                                PayloadB = ge.AbilityId,
                            });
                            break;
                        case GasPresentationEventKind.CastFailed:
                            _stream.TryAdd(new PresentationEvent
                            {
                                LogicTickStamp = tick,
                                Kind = PresentationEventKind.CastFailed,
                                KeyId = ge.AbilityId,
                                Source = ge.Actor,
                                Target = ge.Target,
                                Magnitude = 0f,
                                PayloadA = ge.AbilitySlot,
                                PayloadB = (int)ge.FailReason,
                            });
                            break;
                    }
                }
            }

            // Tag changed bits → PresentationEventStream
            var job = new TagChangedJob
            {
                Stream = _stream,
                Tick = tick
            };
            World.InlineEntityQuery<TagChangedJob, GameplayTagEffectiveChangedBits, GameplayTagEffectiveCache>(in _tagChangedQuery, ref job);
        }

        private struct TagChangedJob : IForEachWithEntity<GameplayTagEffectiveChangedBits, GameplayTagEffectiveCache>
        {
            public PresentationEventStream Stream;
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

                            Stream.TryAdd(new PresentationEvent
                            {
                                LogicTickStamp = Tick,
                                Kind = PresentationEventKind.TagEffectiveChanged,
                                KeyId = tagId,
                                Source = entity,
                                Target = entity,
                                Magnitude = now ? 1f : 0f
                            });
                        }
                    }
                }
            }
        }
    }
}
