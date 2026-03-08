using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using CoreInputMod.ViewMode;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;

namespace CoreInputMod.Triggers
{
    /// <summary>
    /// Registers generic input systems on game start: SelectionCommand, EntityClickSelect,
    /// GasSelectionResponse, GasInputResponse.
    /// Does not include order sources (move/attack/etc) — those are game-mode specific.
    /// </summary>
    public sealed class InstallCoreInputOnGameStartTrigger : Trigger
    {
        private const string InstalledKey = "CoreInputMod.Installed";
        public const string EntitySelectionCallbacksKey = "CoreInputMod.EntitySelectionCallbacks";
        public const string SelectionTriggeredCallbacksKey = "CoreInputMod.SelectionTriggeredCallbacks";
        private readonly IModContext _ctx;

        public InstallCoreInputOnGameStartTrigger(IModContext ctx)
        {
            _ctx = ctx;
            EventKey = GameEvents.GameStart;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue(InstalledKey, out var obj) && obj is bool installed && installed)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[InstalledKey] = true;
            engine.GlobalContext[EntitySelectionCallbacksKey] = new List<Action<WorldCmInt2, Entity>>();
            engine.GlobalContext[SelectionTriggeredCallbacksKey] = new List<Action<SelectionRequest, WorldCmInt2>>();

            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.InteractionActionBindings.Name, out var bindingsObj)
                || bindingsObj is not InteractionActionBindings)
            {
                engine.GlobalContext[CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings();
            }

            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.SelectionRuleRegistry.Name, out var rulesObj)
                || rulesObj is not SelectionRuleRegistry)
            {
                engine.GlobalContext[CoreServiceKeys.SelectionRuleRegistry.Name] = SelectionRuleRegistry.CreateWithDefaults();
            }

            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.SelectionInteractionState.Name, out var interactionObj)
                || interactionObj is not SelectionInteractionState)
            {
                engine.SetService(CoreServiceKeys.SelectionInteractionState, new SelectionInteractionState());
            }

            var selectionCallbacks = (List<Action<WorldCmInt2, Entity>>)engine.GlobalContext[EntitySelectionCallbacksKey];
            var triggeredCallbacks = (List<Action<SelectionRequest, WorldCmInt2>>)engine.GlobalContext[SelectionTriggeredCallbacksKey];

            engine.RegisterSystem(new SelectionCommandSystem(engine.World, engine.GlobalContext), SystemGroup.InputCollection);

            var clickSelect = new EntityClickSelectSystem(engine.World, engine.GlobalContext, engine.SpatialQueries);
            clickSelect.OnEntitySelected = (worldCm, entity) =>
            {
                foreach (var callback in selectionCallbacks)
                {
                    callback(worldCm, entity);
                }
            };
            engine.RegisterSystem(clickSelect, SystemGroup.InputCollection);

            var gasSelection = new GasSelectionResponseSystem(engine.World, engine.GlobalContext, engine.SpatialQueries);
            gasSelection.OnSelectionTriggered = (req, worldCm) =>
            {
                foreach (var callback in triggeredCallbacks)
                {
                    callback(req, worldCm);
                }
            };
            engine.RegisterSystem(gasSelection, SystemGroup.InputCollection);

            engine.RegisterSystem(new GasInputResponseSystem(engine.World, engine.GlobalContext), SystemGroup.InputCollection);

            var viewModeManager = new ViewModeManager(engine.World, engine.GlobalContext, engine.GameSession.Camera);
            engine.GlobalContext[ViewModeManager.GlobalKey] = viewModeManager;

            _ctx.Log("[CoreInputMod] SelectionCommand, EntityClickSelect, GasSelectionResponse, GasInputResponse, and ViewMode registered");
            return Task.CompletedTask;
        }
    }
}
