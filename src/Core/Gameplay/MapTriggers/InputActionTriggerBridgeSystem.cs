using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.MapTriggers
{
    /// <summary>
    /// Bridges authored input actions (Input/trigger_actions.json) into map-scoped
    /// InputActionFired trigger events: press edge -> payload { action, rep =
    /// player representative, ground point, held semantic-modifier bitmask, active
    /// interaction context id }. Actions with pickRadiusCm additionally resolve the
    /// nearest pickable entity around the ground point into the payload and only fire
    /// when a pick exists — TriggerGraphs stay pure data, no mod code.
    /// </summary>
    public sealed class InputActionTriggerBridgeSystem : Arch.System.ISystem<float>
    {
        private readonly World _world;
        private readonly Func<MapSession?> _currentSession;
        private readonly TriggerManager _triggerManager;
        private readonly Func<ScriptContext> _createContext;
        private readonly Func<IInputActionReader?> _reader;
        private readonly IReadOnlyList<InputTriggerAction> _actions;
        private readonly InteractionContextProfileRegistry _contextProfiles;
        private readonly QueryDescription _pickQuery;

        public InputActionTriggerBridgeSystem(
            World world,
            Func<MapSession?> currentSession,
            TriggerManager triggerManager,
            Func<ScriptContext> createContext,
            Func<IInputActionReader?> reader,
            IReadOnlyList<InputTriggerAction> actions,
            InteractionContextProfileRegistry contextProfiles)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _currentSession = currentSession ?? throw new ArgumentNullException(nameof(currentSession));
            _triggerManager = triggerManager ?? throw new ArgumentNullException(nameof(triggerManager));
            _createContext = createContext ?? throw new ArgumentNullException(nameof(createContext));
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _actions = actions ?? Array.Empty<InputTriggerAction>();
            _contextProfiles = contextProfiles ?? throw new ArgumentNullException(nameof(contextProfiles));
            _pickQuery = new QueryDescription().WithAll<WorldPositionCm, AttributeBuffer>();
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            if (_actions.Count == 0)
            {
                return;
            }

            MapSession? session = _currentSession();
            IInputActionReader? input = _reader();
            if (session == null || input == null)
            {
                return;
            }

            Entity playerRep = ResolvePlayerRepresentative(session);
            if (playerRep == Entity.Null || playerRep == default)
            {
                return;
            }

            for (int i = 0; i < _actions.Count; i++)
            {
                InputTriggerAction action = _actions[i];
                if (string.IsNullOrWhiteSpace(action.Id) || !input.PressedThisFrame(action.Id))
                {
                    continue;
                }

                if (!AuthoritativeGroundPointerHelper.TryRead(input, out Ludots.Platform.Abstractions.WorldCmInt2 ground))
                {
                    continue;
                }

                Entity picked = action.PickRadiusCm > 0
                    ? PickNearest(ground, action.PickRadiusCm, playerRep)
                    : Entity.Null;
                if (action.PickRadiusCm > 0 && (picked == Entity.Null || picked == default))
                {
                    // Pick actions fire only on a resolved pick; miss-clicks stay silent.
                    continue;
                }

                ScriptContext context = _createContext();
                context.Set(CoreServiceKeys.MapId, session.MapId);
                context.Set(CoreServiceKeys.MapSession, session);
                context.Set(MapTriggerEventPayloadKeys.Rep, playerRep);
                context.Set(MapTriggerEventPayloadKeys.Action, action.Id);
                context.Set(MapTriggerEventPayloadKeys.GroundPointXCm, (float)ground.X);
                context.Set(MapTriggerEventPayloadKeys.GroundPointYCm, (float)ground.Y);
                context.Set(MapTriggerEventPayloadKeys.Modifiers, ReadHeldModifiers(input));
                context.Set(MapTriggerEventPayloadKeys.ContextId, ResolveActiveContextId(playerRep));
                if (picked != Entity.Null && picked != default)
                {
                    context.Set(MapTriggerEventPayloadKeys.TargetEntity, picked);
                }

                _triggerManager.FireMapEvent(session.MapId, GameEvents.InputActionFired, context);
            }
        }

        private static int ReadHeldModifiers(IInputActionReader input)
        {
            int modifiers = InputActionFiredModifiers.None;
            if (input.IsDown(CommandSourceModifierActionIds.Additive))
            {
                modifiers |= InputActionFiredModifiers.Queue;
            }

            if (input.IsDown(CommandSourceModifierActionIds.Toggle))
            {
                modifiers |= InputActionFiredModifiers.Precision;
            }

            return modifiers;
        }

        private int ResolveActiveContextId(Entity playerRep)
        {
            if (_world.TryGet<ActiveInteractionContext>(playerRep, out ActiveInteractionContext active))
            {
                return active.ContextId;
            }

            // Absence of a mounted context is the steady state: the reserved default
            // profile anchors it; when no profile catalog is installed the honest id is 0.
            return _contextProfiles.ProfileIdRegistry.GetId(InteractionContextIds.Default);
        }

        private Entity ResolvePlayerRepresentative(MapSession session)
        {
            var players = session.MapConfig?.Players;
            if (players == null || players.Count == 0)
            {
                return Entity.Null;
            }

            string? instanceId = players[0].RepresentativeInstanceId;
            if (string.IsNullOrWhiteSpace(instanceId) || session.EntityIndex == null)
            {
                return Entity.Null;
            }

            return session.EntityIndex.GetRequired(session.MapId.Value, instanceId, "InputActionTriggerBridge");
        }

        private Entity PickNearest(Ludots.Platform.Abstractions.WorldCmInt2 ground, int radiusCm, Entity exclude)
        {
            Entity nearest = Entity.Null;
            long best = (long)radiusCm * radiusCm;
            _world.Query(in _pickQuery, (Entity entity, ref WorldPositionCm position) =>
            {
                if (entity == exclude)
                {
                    return;
                }

                float dx = position.Value.X.ToFloat() - ground.X;
                float dy = position.Value.Y.ToFloat() - ground.Y;
                long dist = (long)(dx * dx) + (long)(dy * dy);
                if (dist <= best)
                {
                    best = dist;
                    nearest = entity;
                }
            });
            return nearest;
        }
    }

    /// <summary>One authored bridged action (Input/trigger_actions.json, ArrayById by id).</summary>
    public sealed class InputTriggerAction
    {
        public string Id { get; set; } = string.Empty;
        public int PickRadiusCm { get; set; }
    }
}
