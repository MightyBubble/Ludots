using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using CoreInputMod.Systems;
using CoreInputMod.ViewMode;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;

namespace CoreInputMod.Triggers
{
    /// <summary>
    /// Registers generic input systems on game start: CurrentSelectionApply, GasSelectionResponse, GasInputResponse.
    /// Does not include order sources (move/attack/etc) — those are game-mode specific (MobaDemoMod, RtsDemoMod, etc).
    /// For camera, compose CameraProfilesMod / CameraBootstrapMod / VirtualCameraShotsMod as needed.
    /// Mods can add callbacks via GlobalContext["CoreInputMod.EntitySelectionCallbacks"] and
    /// ["CoreInputMod.SelectionTriggeredCallbacks"] to customize visual feedback.
    /// </summary>
    public sealed class InstallCoreInputOnGameStartTrigger : Trigger
    {
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
            if (engine == null) return Task.CompletedTask;

            if (engine.TryGetService(CoreInputServiceKeys.Installed, out bool installed) && installed)
                return Task.CompletedTask;
            engine.SetService(CoreInputServiceKeys.Installed, true);

            var selectionCallbacks = new List<Action<WorldCmInt2, Entity>>();
            var triggeredCallbacks = new List<Action<SelectionRequest, WorldCmInt2>>();
            engine.SetService(CoreInputServiceKeys.EntitySelectionCallbacks, selectionCallbacks);
            engine.SetService(CoreInputServiceKeys.SelectionTriggeredCallbacks, triggeredCallbacks);

            _ = engine.GetService(CoreServiceKeys.InteractionActionBindings)
                ?? throw new InvalidOperationException("InteractionActionBindings must be registered before CoreInputMod installs.");

            var selectionRules = engine.GetService(CoreServiceKeys.SelectionRuleRegistry)
                ?? throw new InvalidOperationException("SelectionRuleRegistry must be registered before CoreInputMod installs.");
            var selectionRuntime = engine.GetService(CoreServiceKeys.SelectionRuntime)
                ?? throw new InvalidOperationException("SelectionRuntime must be registered before CoreInputMod installs.");
            var orderQueue = engine.GetService(CoreServiceKeys.OrderQueue)
                ?? throw new InvalidOperationException("OrderQueue must be registered before CoreInputMod installs.");

            engine.RegisterSystem(new SelectionMaintenanceSystem(engine.World, selectionRuntime), SystemGroup.InputCollection);
            engine.RegisterSystem(new OrderSelectionLeaseCleanupSystem(engine.World, orderQueue), SystemGroup.Cleanup);

            var currentSelection = new CurrentSelectionApplySystem(engine.World, engine.GlobalContext, selectionRuntime);
            currentSelection.OnEntitySelected = (worldCm, entity) =>
            {
                foreach (var cb in selectionCallbacks) cb(worldCm, entity);
            };
            engine.InsertSystemBeforeRequired<CameraRuntimeSystem>(currentSelection, SystemGroup.InputCollection);

            var gasSelection = new GasSelectionResponseSystem(engine.World, engine.GlobalContext, engine.SpatialQueries, selectionRules);
            gasSelection.OnSelectionTriggered = (req, worldCm) =>
            {
                foreach (var cb in triggeredCallbacks) cb(req, worldCm);
            };
            engine.RegisterSystem(gasSelection, SystemGroup.InputCollection);

            engine.RegisterSystem(new GasInputResponseSystem(engine.World, engine.GlobalContext), SystemGroup.InputCollection);
            engine.RegisterSystem(new AbilityExecAimSyncSystem(engine.World, new InputInteractionContextAccessor(engine.World, engine.GlobalContext)), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new SkillBarOverlaySystem(engine.World, engine.GlobalContext));
            engine.RegisterPresentationSystem(new SelectionBoxOverlaySystem(engine.World, engine.GlobalContext));
            engine.RegisterPresentationSystem(new AbilityAimOverlayPresentationSystem(engine.World, engine.GlobalContext));
            engine.RegisterPresentationSystem(new SelectedMovePathPresentationSystem(engine.World, engine.GlobalContext, selectionRuntime));
            engine.RegisterSystem(new TabTargetCycleSystem(engine.World, engine.GlobalContext), SystemGroup.InputCollection);

            var vmManager = new ViewModeManager(engine.GlobalContext);
            engine.SetService(CoreInputServiceKeys.ViewModeManager, vmManager);
            engine.RegisterSystem(new ViewModeSwitchSystem(engine.GlobalContext), SystemGroup.InputCollection);

            _ctx.Log("[CoreInputMod] CurrentSelectionApply, GasSelectionResponse, GasInputResponse, SkillBar, SelectionBox, AbilityAimOverlay, SelectedMovePathOverlay, TabTarget, ViewMode registered");
            return Task.CompletedTask;
        }
    }
}
