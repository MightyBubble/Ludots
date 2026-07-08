using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using CoreInputMod.Systems;
using CoreInputMod.ViewMode;
using Ludots.Core.Engine;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;

namespace CoreInputMod.Triggers
{
    /// <summary>
    /// Registers generic input systems on game start: CommandSourceAcquisition, GasInputResponse.
    /// Does not include order sources (move/attack/etc) — those are game-mode specific (MobaDemoMod, RtsDemoMod, etc).
    /// For camera, compose CameraProfilesMod / CameraBootstrapMod / VirtualCameraShotsMod as needed.
    /// Mods can add callbacks via GlobalContext["CoreInputMod.EntitySelectionCallbacks"] to customize visual feedback.
    /// </summary>
    public sealed class InstallCoreInputOnGameStartTrigger : Trigger
    {
        public const string EntitySelectionCallbacksKey = "CoreInputMod.EntitySelectionCallbacks";
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
            engine.SetService(CoreInputServiceKeys.EntitySelectionCallbacks, selectionCallbacks);

            _ = engine.GetService(CoreServiceKeys.InteractionActionBindings)
                ?? throw new InvalidOperationException("InteractionActionBindings must be registered before CoreInputMod installs.");

            _ = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore must be registered before CoreInputMod installs.");
            _ = engine.GetService(CoreServiceKeys.CommandSourceAcquisitionConfig)
                ?? throw new InvalidOperationException("CommandSourceAcquisitionConfig must be registered before CoreInputMod installs.");

            var commandSourceAcquisition = new CommandSourceAcquisitionSystem(engine.World, engine.GlobalContext);
            commandSourceAcquisition.OnEntitySelected = (worldCm, entity) =>
            {
                foreach (var cb in selectionCallbacks) cb(worldCm, entity);
            };
            engine.InsertSystemBeforeRequired<CameraRuntimeSystem>(commandSourceAcquisition, SystemGroup.InputCollection);

            engine.RegisterSystem(new GasInputResponseSystem(engine.World, engine.GlobalContext), SystemGroup.InputCollection);
            engine.RegisterSystem(new AbilityExecAimSyncSystem(engine.World, new InputInteractionContextAccessor(engine.World, engine.GlobalContext)), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new SkillBarOverlaySystem(engine.World, engine.GlobalContext));
            engine.RegisterPresentationSystem(new SelectionBoxOverlaySystem(engine.World, engine.GlobalContext));
            engine.InsertPresentationSystemBefore<EntityCollectionPresentationEventSystem>(new AbilityAimPresentationProjectionSystem(engine.World, engine.GlobalContext));
            engine.InsertPresentationSystemBefore<PerformerRuleSystem>(new SelectedMovePathPresentationSystem(engine.World, engine.GlobalContext));
            engine.RegisterSystem(new TabTargetCycleSystem(engine.World, engine.GlobalContext), SystemGroup.InputCollection);

            var vmManager = new ViewModeManager(engine.GlobalContext);
            engine.SetService(CoreInputServiceKeys.ViewModeManager, vmManager);
            RegisterLoadedModViewModes(engine);
            engine.RegisterSystem(new ViewModeSwitchSystem(engine.GlobalContext), SystemGroup.InputCollection);

            _ctx.Log("[CoreInputMod] CommandSourceAcquisition, GasInputResponse, SkillBar, SelectionBox, AbilityAimPresentation, SelectedMovePathPresentation, TabTarget, ViewMode registered");
            return Task.CompletedTask;
        }

        private void RegisterLoadedModViewModes(GameEngine engine)
        {
            if (engine.ModLoader?.LoadedModIds == null)
            {
                return;
            }

            for (int i = 0; i < engine.ModLoader.LoadedModIds.Count; i++)
            {
                string modId = engine.ModLoader.LoadedModIds[i];
                ViewModeRegistrar.RegisterFromVfs(
                    _ctx,
                    engine.GlobalContext,
                    sourceModId: modId,
                    activateWhenUnset: false);
            }
        }
    }
}
