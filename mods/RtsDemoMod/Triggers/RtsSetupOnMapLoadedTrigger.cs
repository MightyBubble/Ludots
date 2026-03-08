using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using CoreInputMod.ViewMode;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using RtsDemoMod.Systems;

namespace RtsDemoMod.Triggers
{
    public sealed class RtsSetupOnMapLoadedTrigger : Trigger
    {
        private readonly IModContext _ctx;

        public RtsSetupOnMapLoadedTrigger(IModContext ctx)
        {
            _ctx = ctx;
            EventKey = GameEvents.MapLoaded;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            var mapTags = context.Get(CoreServiceKeys.MapTags) ?? new List<string>();
            bool isRtsMap = HasTag(mapTags, "rts");
            engine.GlobalContext[RtsDemoKeys.IsActiveMap] = isRtsMap;
            if (!isRtsMap)
            {
                engine.GlobalContext.Remove(CoreServiceKeys.SelectionInputHandler.Name);
                engine.GlobalContext.Remove(CoreServiceKeys.SelectionCandidatePolicy.Name);
                engine.GlobalContext.Remove(CoreServiceKeys.ActiveSelectionProfileId.Name);
                if (engine.GlobalContext.TryGetValue(CoreServiceKeys.SelectionInteractionState.Name, out var inactiveObj)
                    && inactiveObj is SelectionInteractionState inactiveState)
                {
                    inactiveState.ClearPreview();
                }

                return Task.CompletedTask;
            }

            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.InteractionActionBindings.Name, out var bindingsObj)
                || bindingsObj is not InteractionActionBindings bindings)
            {
                bindings = new InteractionActionBindings();
                engine.GlobalContext[CoreServiceKeys.InteractionActionBindings.Name] = bindings;
            }

            bindings.ConfirmActionId = "Select";
            bindings.CommandActionId = "Command";
            bindings.CancelActionId = "Cancel";
            bindings.PointerPositionActionId = "PointerPos";

            if (engine.GlobalContext.TryGetValue(ViewModeManager.GlobalKey, out var viewModeObj) && viewModeObj is ViewModeManager viewModeManager)
            {
                viewModeManager.SwitchTo("Rts");
            }

            var controller = RtsUnitRuntimeSetup.EnsureController(engine.World, engine.GlobalContext);
            engine.GlobalContext[CoreServiceKeys.LocalPlayerEntity.Name] = controller;
            SelectionRuntime.ClearSelection(engine.World, engine.GlobalContext, controller);

            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.InputHandler.Name, out var inputObj) && inputObj is PlayerInputHandler input)
            {
                var selectionInteractionState = engine.GetService(CoreServiceKeys.SelectionInteractionState) ?? new SelectionInteractionState();
                selectionInteractionState.ClearPreview();
                engine.SetService(CoreServiceKeys.SelectionInteractionState, selectionInteractionState);
                engine.SetService(CoreServiceKeys.SelectionInputHandler, new ScreenSelectionInputHandler(engine.GlobalContext, input, selectionInteractionState));
                engine.SetService(CoreServiceKeys.SelectionCandidatePolicy, new RtsSelectionCandidatePolicy());
            }

            var query = new QueryDescription().WithAll<Name, WorldPositionCm>();
            engine.World.Query(in query, (Entity entity, ref Name _, ref WorldPositionCm _) =>
            {
                if (!engine.World.Has<GameplayTagContainer>(entity))
                {
                    engine.World.Add(entity, new GameplayTagContainer());
                }

                RtsUnitRuntimeSetup.EnsureRuntimeComponents(engine.World, entity);
            });

            _ctx.Log("[RtsDemoMod] RTS map activated; controller, camera view mode, shared selection, and runtime navigation components are ready.");
            return Task.CompletedTask;
        }

        private static bool HasTag(List<string> tags, string tag)
        {
            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], tag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
