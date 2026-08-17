using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace Ludots.Core.Gameplay.GAS.LiveSkillWorkbench
{
    public enum LiveEffectChainPhase : byte
    {
        Cast = 1,
        Effect = 2,
        Attribute = 3,
        Tag = 4,
        Graph = 5,
        Response = 6,
        Dropped = 7
    }

    public readonly struct LiveEffectChainEvent
    {
        public LiveEffectChainEvent(
            Guid traceId,
            LiveEffectChainPhase phase,
            string label,
            string? definitionId,
            string? detail,
            int sequence,
            int actorEntityId,
            int targetEntityId)
        {
            TraceId = traceId;
            Phase = phase;
            Label = label;
            DefinitionId = definitionId;
            Detail = detail;
            Sequence = sequence;
            ActorEntityId = actorEntityId;
            TargetEntityId = targetEntityId;
        }

        public Guid TraceId { get; }
        public LiveEffectChainPhase Phase { get; }
        public string Label { get; }
        public string? DefinitionId { get; }
        public string? Detail { get; }
        public int Sequence { get; }
        public int ActorEntityId { get; }
        public int TargetEntityId { get; }
    }

    /// <summary>
    /// #621: Bounded effect-chain timeline. Fail-closed overflow reports Dropped events (no silent loss).
    /// </summary>
    public sealed class LiveEffectChainTracer
    {
        private readonly LiveEffectChainEvent[] _ring;
        private readonly Dictionary<int, Guid> _openCastByActor = new();
        private int _write;
        private int _count;
        private int _sequence;
        private int _dropped;

        public LiveEffectChainTracer(int capacity = 256)
        {
            if (capacity < 16)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Tracer capacity must be >= 16.");
            }

            _ring = new LiveEffectChainEvent[capacity];
        }

        public int Capacity => _ring.Length;
        public int Count => _count;
        public int DroppedCount => _dropped;

        public Guid BeginTrace(Entity actor)
        {
            Guid id = Guid.NewGuid();
            _openCastByActor[actor.Id] = id;
            return id;
        }

        public void IngestPresentationEvents(ReadOnlySpan<GasPresentationEvent> events)
        {
            for (int i = 0; i < events.Length; i++)
            {
                Ingest(in events[i]);
            }
        }

        public void Ingest(in GasPresentationEvent evt)
        {
            Guid traceId = ResolveTrace(in evt);
            switch (evt.Kind)
            {
                case GasPresentationEventKind.CastStarted:
                    Record(traceId, LiveEffectChainPhase.Cast, "Cast started",
                        AbilityIdRegistry.GetName(evt.AbilityId),
                        $"slot={evt.AbilitySlot}", evt.Actor.Id, evt.Target.Id);
                    break;
                case GasPresentationEventKind.CastCommitted:
                    Record(traceId, LiveEffectChainPhase.Cast, "Cast committed",
                        AbilityIdRegistry.GetName(evt.AbilityId), null, evt.Actor.Id, evt.Target.Id);
                    break;
                case GasPresentationEventKind.CastFailed:
                    Record(traceId, LiveEffectChainPhase.Cast, "Cast failed",
                        AbilityIdRegistry.GetName(evt.AbilityId),
                        evt.FailReason.ToString(), evt.Actor.Id, evt.Target.Id);
                    break;
                case GasPresentationEventKind.CastFinished:
                    Record(traceId, LiveEffectChainPhase.Cast, "Cast finished",
                        AbilityIdRegistry.GetName(evt.AbilityId), null, evt.Actor.Id, evt.Target.Id);
                    _openCastByActor.Remove(evt.Actor.Id);
                    break;
                case GasPresentationEventKind.EffectApplied:
                    Record(traceId, LiveEffectChainPhase.Effect, "Effect applied",
                        EffectTemplateIdRegistry.GetName(evt.EffectTemplateId), null, evt.Actor.Id, evt.Target.Id);
                    break;
                case GasPresentationEventKind.EffectActivated:
                    Record(traceId, LiveEffectChainPhase.Effect, "Effect activated",
                        EffectTemplateIdRegistry.GetName(evt.EffectTemplateId), null, evt.Actor.Id, evt.Target.Id);
                    break;
                case GasPresentationEventKind.EffectExpired:
                    Record(traceId, LiveEffectChainPhase.Effect, "Effect expired",
                        EffectTemplateIdRegistry.GetName(evt.EffectTemplateId), null, evt.Actor.Id, evt.Target.Id);
                    break;
                case GasPresentationEventKind.EffectCancelled:
                    Record(traceId, LiveEffectChainPhase.Effect, "Effect cancelled",
                        EffectTemplateIdRegistry.GetName(evt.EffectTemplateId), null, evt.Actor.Id, evt.Target.Id);
                    break;
                default:
                    Record(traceId, LiveEffectChainPhase.Effect, evt.Kind.ToString(),
                        null, null, evt.Actor.Id, evt.Target.Id);
                    break;
            }

            if (evt.AttributeId != 0 && Math.Abs(evt.Delta) > 0f)
            {
                Record(traceId, LiveEffectChainPhase.Attribute, "Attribute delta",
                    AttributeRegistry.GetName(evt.AttributeId),
                    evt.Delta.ToString("0.###"), evt.Actor.Id, evt.Target.Id);
            }
        }

        public void RecordGraph(Guid traceId, int graphId, string label, int actorId, int targetId)
        {
            string name = GraphIdRegistry.GetName(graphId);
            Record(traceId, LiveEffectChainPhase.Graph, label,
                string.IsNullOrEmpty(name) ? $"graph:{graphId}" : name,
                null, actorId, targetId);
        }

        public void RecordResponse(Guid traceId, string label, string? detail, int actorId, int targetId)
        {
            Record(traceId, LiveEffectChainPhase.Response, label, null, detail, actorId, targetId);
        }

        public void RecordTag(Guid traceId, string tagName, string label, int actorId, int targetId)
        {
            Record(traceId, LiveEffectChainPhase.Tag, label, tagName, null, actorId, targetId);
        }

        public IReadOnlyList<LiveEffectChainEvent> SnapshotRecent(int max = 64)
        {
            if (max <= 0) throw new ArgumentOutOfRangeException(nameof(max));
            int take = Math.Min(max, _count);
            var result = new LiveEffectChainEvent[take];
            for (int i = 0; i < take; i++)
            {
                int idx = (_write - take + i + _ring.Length * 2) % _ring.Length;
                result[i] = _ring[idx];
            }

            return result;
        }

        public IReadOnlyList<LiveEffectChainEvent> QueryByTraceId(Guid traceId, int max = 128)
        {
            var list = new List<LiveEffectChainEvent>(Math.Min(max, _count));
            for (int i = 0; i < _count && list.Count < max; i++)
            {
                int idx = (_write - _count + i + _ring.Length * 2) % _ring.Length;
                if (_ring[idx].TraceId == traceId)
                {
                    list.Add(_ring[idx]);
                }
            }

            return list;
        }

        public IReadOnlyList<LiveEffectChainEvent> QueryByActor(int actorEntityId, int max = 64)
        {
            var list = new List<LiveEffectChainEvent>(Math.Min(max, _count));
            for (int i = 0; i < _count && list.Count < max; i++)
            {
                int idx = (_write - _count + i + _ring.Length * 2) % _ring.Length;
                if (_ring[idx].ActorEntityId == actorEntityId || _ring[idx].TargetEntityId == actorEntityId)
                {
                    list.Add(_ring[idx]);
                }
            }

            return list;
        }

        private Guid ResolveTrace(in GasPresentationEvent evt)
        {
            if (_openCastByActor.TryGetValue(evt.Actor.Id, out Guid existing))
            {
                return existing;
            }

            Guid id = Guid.NewGuid();
            if (evt.Kind is GasPresentationEventKind.CastStarted or GasPresentationEventKind.CastCommitted)
            {
                _openCastByActor[evt.Actor.Id] = id;
            }

            return id;
        }

        private void Record(
            Guid traceId,
            LiveEffectChainPhase phase,
            string label,
            string? definitionId,
            string? detail,
            int actorId,
            int targetId)
        {
            if (_count >= _ring.Length)
            {
                _dropped++;
                // Overwrite oldest but also emit an explicit Dropped marker periodically.
                if ((_dropped % 8) == 1)
                {
                    WriteSlot(new LiveEffectChainEvent(
                        traceId,
                        LiveEffectChainPhase.Dropped,
                        "Trace buffer full",
                        null,
                        $"dropped={_dropped}",
                        ++_sequence,
                        actorId,
                        targetId));
                }
            }

            WriteSlot(new LiveEffectChainEvent(
                traceId,
                phase,
                label,
                definitionId,
                detail,
                ++_sequence,
                actorId,
                targetId));
        }

        private void WriteSlot(in LiveEffectChainEvent evt)
        {
            _ring[_write] = evt;
            _write = (_write + 1) % _ring.Length;
            if (_count < _ring.Length)
            {
                _count++;
            }
        }
    }
}
