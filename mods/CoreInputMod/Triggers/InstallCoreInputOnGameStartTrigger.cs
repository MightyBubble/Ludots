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
    /// Mods can add callbacks via GlobalContext["CoreInputMod.CommandSourceAcquiredCallbacks"] to customize visual feedback.
    /// </summary>
    public sealed class InstallCoreInputOnGameStartTrigger : Trigger
    {
        public const string CommandSourceAcquiredCallbacksKey = "CoreInputMod.CommandSourceAcquiredCallbacks";
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

            var commandSourceAcquiredCallbacks = new List<Action<WorldCmInt2, Entity>>();
            engine.SetService(CoreInputServiceKeys.CommandSourceAcquiredCallbacks, commandSourceAcquiredCallbacks);

            _ = engine.GetService(CoreServiceKeys.InteractionActionBindings)
                ?? throw new InvalidOperationException("InteractionActionBindings must be registered before CoreInputMod installs.");

            _ = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore must be registered before CoreInputMod installs.");
            var commandSourceAcquisitionConfig = engine.GetService(CoreServiceKeys.CommandSourceAcquisitionConfig)
                ?? throw new InvalidOperationException("CommandSourceAcquisitionConfig must be registered before CoreInputMod installs.");

            var commandSourceAcquisition = new CommandSourceAcquisitionSystem(
                engine.World,
                engine.GlobalContext,
                (out Entity owner) => TryResolveLocalCommandSourceOwner(engine, out owner));
            commandSourceAcquisition.OnEntityAcquired = (worldCm, entity) =>
            {
                foreach (var cb in commandSourceAcquiredCallbacks) cb(worldCm, entity);
            };
            engine.InsertSystemBeforeRequired<CameraRuntimeSystem>(commandSourceAcquisition, SystemGroup.InputCollection);

            engine.RegisterSystem(new GasInputResponseSystem(engine.World, engine.GlobalContext), SystemGroup.InputCollection);
            engine.RegisterSystem(new AbilityExecAimSyncSystem(engine.World, new InputInteractionContextAccessor(engine.World, engine.GlobalContext)), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new SkillBarOverlaySystem(
                engine.World,
                engine.GlobalContext,
                (out Entity owner) => TryResolveLocalCommandSourceOwner(engine, out owner)));
            engine.RegisterPresentationSystem(new CommandSourceDragOverlaySystem(
                engine.World,
                engine.GlobalContext,
                (out Entity owner) => TryResolveLocalCommandSourceOwner(engine, out owner),
                commandSourceAcquisitionConfig));
            engine.InsertPresentationSystemBefore<EntityCollectionPresentationEventSystem>(new AbilityAimPresentationProjectionSystem(engine.World, engine.GlobalContext));
            engine.InsertPresentationSystemBefore<PerformerRuleSystem>(new CommandActorMovePathPresentationSystem(
                engine.World,
                engine.GlobalContext,
                (out Entity owner) => TryResolveLocalCommandSourceOwner(engine, out owner)));
            engine.RegisterSystem(new TabTargetCycleSystem(engine.World, engine.GlobalContext), SystemGroup.InputCollection);

            var vmManager = new ViewModeManager(engine.World, engine.GlobalContext);
            engine.SetService(CoreInputServiceKeys.ViewModeManager, vmManager);
            RegisterLoadedModViewModes(engine);
            engine.RegisterSystem(new ViewModeSwitchSystem(engine.GlobalContext), SystemGroup.InputCollection);

            _ctx.Log("[CoreInputMod] CommandSourceAcquisition, GasInputResponse, SkillBar, CommandSourceDragOverlay, AbilityAimPresentation, CommandActorMovePathPresentation, TabTarget, ViewMode registered");
            return Task.CompletedTask;
        }

        private static bool TryResolveLocalCommandSourceOwner(GameEngine engine, out Entity owner)
        {
            owner = Entity.Null;
            Entity local = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
            if (local == Entity.Null || !engine.World.IsAlive(local))
            {
                return false;
            }

            owner = local;
            return true;
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
