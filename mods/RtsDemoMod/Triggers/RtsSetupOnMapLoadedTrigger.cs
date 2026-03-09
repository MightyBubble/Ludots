using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using CoreInputMod.Systems;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
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
        private const string DefaultInputContextId = "Default_Gameplay";

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

            var input = engine.GetService(CoreServiceKeys.InputHandler)
                ?? throw new InvalidOperationException("RTS demo requires an active PlayerInputHandler.");

            if (!isRtsMap)
            {
                DeactivateRts(engine, input);
                return Task.CompletedTask;
            }

            ConfigureRtsBindings(engine);
            ConfigureRtsInputContext(input);
            EnsureSelectionProfile(engine);

            var interactionState = engine.GetService(CoreServiceKeys.SelectionInteractionState) ?? new SelectionInteractionState();
            interactionState.ClearPreview();
            engine.SetService(CoreServiceKeys.SelectionInteractionState, interactionState);
            engine.SetService(CoreServiceKeys.SelectionInputHandler, new ScreenSelectionInputHandler(engine.GlobalContext, input, interactionState));
            engine.SetService(CoreServiceKeys.SelectionCandidatePolicy, new RtsSelectionCandidatePolicy());
            engine.SetService(CoreServiceKeys.CameraPresetRequest, new CameraPresetRequest
            {
                PresetId = RtsDemoKeys.CameraPresetId,
                ClearActiveVirtualCamera = true,
            });

            var controller = RtsUnitRuntimeSetup.EnsureController(engine.World, engine.GlobalContext);
            engine.SetService(CoreServiceKeys.LocalPlayerEntity, controller);
            SelectionRuntime.ClearSelection(engine.World, engine.GlobalContext, controller);

            var query = new QueryDescription().WithAll<Name, WorldPositionCm>();
            engine.World.Query(in query, (Entity entity, ref Name _, ref WorldPositionCm _) =>
            {
                if (!engine.World.Has<GameplayTagContainer>(entity))
                {
                    engine.World.Add(entity, new GameplayTagContainer());
                }

                RtsUnitRuntimeSetup.EnsureRuntimeComponents(engine.World, entity);
            });

            _ctx.Log("[RtsDemoMod] RTS map activated; core camera preset, RTS input context, selection pipeline, and runtime navigation components are ready.");
            return Task.CompletedTask;
        }

        private static void ConfigureRtsBindings(GameEngine engine)
        {
            var bindings = engine.GetService(CoreServiceKeys.InteractionActionBindings) ?? new InteractionActionBindings();
            bindings.ConfirmActionId = "Select";
            bindings.CommandActionId = "Command";
            bindings.CancelActionId = "Cancel";
            bindings.PointerPositionActionId = "PointerPos";
            engine.SetService(CoreServiceKeys.InteractionActionBindings, bindings);
        }

        private static void ConfigureRtsInputContext(PlayerInputHandler input)
        {
            if (!input.HasContext(RtsDemoKeys.InputContextId))
            {
                throw new InvalidOperationException($"Missing RTS input context '{RtsDemoKeys.InputContextId}'.");
            }

            input.PopContext(DefaultInputContextId);
            input.PushContext(RtsDemoKeys.InputContextId);
        }

        private static void EnsureSelectionProfile(GameEngine engine)
        {
            var registry = engine.GetService(CoreServiceKeys.SelectionProfileRegistry)
                ?? throw new InvalidOperationException("SelectionProfileRegistry is required for RTS selection.");
            if (registry.Get(RtsDemoKeys.SelectionProfileId) == null)
            {
                throw new InvalidOperationException($"Missing RTS selection profile '{RtsDemoKeys.SelectionProfileId}'.");
            }

            engine.SetService(CoreServiceKeys.ActiveSelectionProfileId, RtsDemoKeys.SelectionProfileId);
        }

        private static void DeactivateRts(GameEngine engine, PlayerInputHandler input)
        {
            input.PopContext(RtsDemoKeys.InputContextId);
            if (input.HasContext(DefaultInputContextId))
            {
                input.PushContext(DefaultInputContextId);
            }

            engine.GlobalContext.Remove(LocalOrderSourceHelper.ActiveMappingKey);
            engine.GlobalContext.Remove(CoreServiceKeys.SelectionInputHandler.Name);
            engine.GlobalContext.Remove(CoreServiceKeys.SelectionCandidatePolicy.Name);
            engine.GlobalContext.Remove(CoreServiceKeys.ActiveSelectionProfileId.Name);
            if (engine.GetService(CoreServiceKeys.SelectionInteractionState) is SelectionInteractionState state)
            {
                state.ClearPreview();
            }
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
