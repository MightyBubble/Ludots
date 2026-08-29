using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Arch.Core;
using DualSeatPanelsShowcaseMod;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelActivation;
using Ludots.Core.UI.PanelHosting;
using Ludots.Core.UI.PanelProjection;

namespace DualSeatPanelsShowcaseMod.Runtime
{
    /// <summary>
    /// Seat-attributed panel operations for the dual-seat showcase: per-seat hotkeys are
    /// read from each seat channel's frozen tick snapshot (<c>channel.Reader</c>,
    /// <c>PressedThisTick</c> — the handler's own frame edge only spans one visual frame
    /// and is lost when the pacemaker skips a logic tick), fired through
    /// <see cref="PanelEventDispatcher.FireFromSeat"/> (audience admission), and admitted
    /// payloads fan out as map custom events consumed by the showcase trigger graphs —
    /// this system never mutates gameplay itself. The shared panel's audience rotation
    /// goes through <see cref="PanelActivationApi"/>, the same write entry the
    /// SetPanelAudience graph op uses.
    /// </summary>
    internal sealed class DualSeatPanelEventSystem : Arch.System.ISystem<float>
    {
        private const int BoostAmount = 10;
        private const int StrikeAmount = -10;
        private const int PokeAmount = 999;
        private const int ChargeAmount = 1;

        private readonly GameEngine _engine;
        private readonly DualSeatPanelsFeedback _feedback;
        private readonly Dictionary<(string PanelId, string SeatId), PanelEventDispatcher> _dispatchers = new();

        public DualSeatPanelEventSystem(GameEngine engine, DualSeatPanelsFeedback feedback)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _feedback = feedback ?? throw new ArgumentNullException(nameof(feedback));
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() => _dispatchers.Clear();

        public void Update(in float dt)
        {
            string? mapId = _engine.CurrentMapSession?.MapId.Value;
            if (!DualSeatPanelsShowcaseIds.IsShowcaseMap(mapId))
            {
                return;
            }

            if (_engine.GetService(CoreServiceKeys.ClientLocalSeatInputRuntime) is not ClientLocalSeatInputRuntime seatInput ||
                _engine.GetService(CoreServiceKeys.ClientLocalSeatRegistry) is not ClientLocalSeatRegistry seats)
            {
                return;
            }

            foreach (string seatId in SeatIds)
            {
                if (!seatInput.TryGetChannel(seatId, out ClientLocalSeatInputChannel channel) ||
                    !seats.TryGet(seatId, out ClientLocalSeat seat) ||
                    seat.PossessedRep == Entity.Null)
                {
                    continue;
                }

                // The panel-of-the-seat is operated by its owner; the cross-seat poke
                // deliberately targets the other seat's panel to make the refusal visible.
                string ownPanelId = seatId == DualSeatPanelsShowcaseIds.SeatZero
                    ? DualSeatPanelsShowcaseIds.SeatZeroPanelId
                    : DualSeatPanelsShowcaseIds.SeatOnePanelId;
                string otherPanelId = seatId == DualSeatPanelsShowcaseIds.SeatZero
                    ? DualSeatPanelsShowcaseIds.SeatOnePanelId
                    : DualSeatPanelsShowcaseIds.SeatZeroPanelId;

                if (channel.Reader.PressedThisTick(DualSeatPanelsShowcaseIds.BoostAction))
                {
                    FirePanelEvent(ownPanelId, seatId, DualSeatPanelsShowcaseIds.ModifyEventId, BoostAmount);
                }

                if (channel.Reader.PressedThisTick(DualSeatPanelsShowcaseIds.StrikeAction))
                {
                    FirePanelEvent(ownPanelId, seatId, DualSeatPanelsShowcaseIds.ModifyEventId, StrikeAmount);
                }

                if (channel.Reader.PressedThisTick(DualSeatPanelsShowcaseIds.PokeAction))
                {
                    FirePanelEvent(otherPanelId, seatId, DualSeatPanelsShowcaseIds.ModifyEventId, PokeAmount);
                }

                if (channel.Reader.PressedThisTick(DualSeatPanelsShowcaseIds.ChargeAction))
                {
                    FirePanelEvent(DualSeatPanelsShowcaseIds.SharedPanelId, seatId, DualSeatPanelsShowcaseIds.ChargeEventId, ChargeAmount);
                }

                if (channel.Reader.PressedThisTick(DualSeatPanelsShowcaseIds.RotateTurnAction))
                {
                    RotateSharedAudience(seatId);
                }
            }
        }

        private static readonly string[] SeatIds =
        {
            DualSeatPanelsShowcaseIds.SeatZero,
            DualSeatPanelsShowcaseIds.SeatOne,
        };

        private void FirePanelEvent(string panelId, string firingSeatId, string eventId, int amount)
        {
            PanelEventDispatcher? dispatcher = TryGetDispatcher(panelId, firingSeatId);
            if (dispatcher == null)
            {
                return;
            }

            var args = new JsonObject { ["amount"] = amount };
            PanelEventFireResult result = dispatcher.FireFromSeat(eventId, args, firingSeatId);
            _feedback.Record(new DualSeatPanelOutcome(
                firingSeatId,
                panelId,
                eventId,
                result.Admitted,
                result.Reason));
        }

        /// <summary>
        /// One dispatcher per (panel, firing seat): admission is the same template audience
        /// either way, but the sink's effect target differs — per-seat panels always settle
        /// on the panel owner's rep, the shared panel charges a map variable with the firing
        /// seat's rep as attribution source.
        /// </summary>
        private PanelEventDispatcher? TryGetDispatcher(string panelId, string firingSeatId)
        {
            var key = (panelId, firingSeatId);
            if (_dispatchers.TryGetValue(key, out PanelEventDispatcher cached))
            {
                return cached;
            }

            if (_engine.GetService(CoreServiceKeys.PanelTemplateRegistry) is not PanelTemplateRegistry templates ||
                !templates.TryGet(panelId, out PanelTemplate template) ||
                _engine.GetService(CoreServiceKeys.PanelActivationStore) is not UiPanelActivationStore activation)
            {
                return null;
            }

            Entity effectTarget = ResolveEffectTarget(panelId, firingSeatId);
            string customEvent = panelId == DualSeatPanelsShowcaseIds.SharedPanelId
                ? DualSeatPanelsShowcaseIds.SharedChargeUsedEvent
                : DualSeatPanelsShowcaseIds.BoostUsedEvent;

            var dispatcher = new PanelEventDispatcher(
                template,
                (eventId, payload) => FireCustomEvent(customEvent, effectTarget, payload),
                activation);
            _dispatchers[key] = dispatcher;
            return dispatcher;
        }

        private Entity ResolveEffectTarget(string panelId, string firingSeatId)
        {
            if (_engine.GetService(CoreServiceKeys.ClientLocalSeatRegistry) is not ClientLocalSeatRegistry seats)
            {
                return Entity.Null;
            }

            string targetSeatId = panelId == DualSeatPanelsShowcaseIds.SharedPanelId
                ? firingSeatId
                : panelId == DualSeatPanelsShowcaseIds.SeatZeroPanelId
                    ? DualSeatPanelsShowcaseIds.SeatZero
                    : DualSeatPanelsShowcaseIds.SeatOne;
            return seats.TryGet(targetSeatId, out ClientLocalSeat seat) ? seat.PossessedRep : Entity.Null;
        }

        private void FireCustomEvent(string eventName, Entity target, IReadOnlyDictionary<string, object?> payload)
        {
            MapSession? session = _engine.CurrentMapSession;
            var registry = _engine.GetService(CoreServiceKeys.CustomEventNameRegistry);
            if (session == null || registry == null || target == Entity.Null || !_engine.World.IsAlive(target))
            {
                return;
            }

            if (!payload.TryGetValue("amount", out object? rawAmount) || rawAmount is not int amount)
            {
                return;
            }

            var context = _engine.CreateContext();
            context.Set(CoreServiceKeys.MapId, session.MapId);
            context.Set(CoreServiceKeys.MapSession, session);
            context.Set(MapTriggerEventPayloadKeys.SourceEntity, target);
            context.Set("DSP.BoostTarget", target);
            context.Set("DSP.Amount", (float)amount);
            _engine.TriggerManager.FireMapCustomEvent(session.MapId, eventName, context, registry);
        }

        /// <summary>
        /// Hotseat rotation over the shared panel: declared audience → seat.0 only →
        /// seat.1 only → back to declared. The activation store override is the same
        /// entry the SetPanelAudience graph op writes; presentation mounts and admission
        /// follow it on the next frames.
        /// </summary>
        private void RotateSharedAudience(string firingSeatId)
        {
            if (_engine.GetService(CoreServiceKeys.PanelActivationApi) is not PanelActivationApi api ||
                _engine.GetService(CoreServiceKeys.PanelActivationStore) is not UiPanelActivationStore activation)
            {
                return;
            }

            string next;
            if (activation.TryGetAudienceOverride(DualSeatPanelsShowcaseIds.SharedPanelId, out PanelAudience current) &&
                current.SeatIds.Count == 1)
            {
                next = current.SeatIds[0] == DualSeatPanelsShowcaseIds.SeatZero
                    ? DualSeatPanelsShowcaseIds.SeatOne
                    : null!;
            }
            else
            {
                next = DualSeatPanelsShowcaseIds.SeatZero;
            }

            if (next == null)
            {
                api.ClearPanelAudience(DualSeatPanelsShowcaseIds.SharedPanelId);
            }
            else
            {
                api.SetPanelAudience(DualSeatPanelsShowcaseIds.SharedPanelId, PanelAudience.Seats(new[] { next }));
            }

            _feedback.Record(new DualSeatPanelOutcome(
                firingSeatId,
                DualSeatPanelsShowcaseIds.SharedPanelId,
                "(rotate)",
                Admitted: true,
                Reason: null));
        }
    }
}
