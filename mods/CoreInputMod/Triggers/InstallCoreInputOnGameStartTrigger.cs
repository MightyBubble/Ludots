using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using CoreInputMod.Systems;
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
    /// Registers generic input systems on game start: EntityClickSelect, GasSelectionResponse, GasInputResponse.
    /// Does not include order sources (move/attack/etc) — those are game-mode specific (MobaDemoMod, RtsDemoMod, etc).
    /// For camera, compose CameraProfilesMod / CameraBootstrapMod / VirtualCameraShotsMod as needed.
    /// Mods can add callbacks via GlobalContext["CoreInputMod.EntitySelectionCallbacks"] and
    /// ["CoreInputMod.SelectionTriggeredCallbacks"] to customize visual feedback.
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
            if (engine == null) return Task.CompletedTask;

            if (engine.GlobalContext.TryGetValue(InstalledKey, out var obj) && obj is bool b && b)
                return Task.CompletedTask;
            engine.GlobalContext[InstalledKey] = true;

            var selectionCallbacks = new List<Action<WorldCmInt2, Entity>>();
            var triggeredCallbacks = new List<Action<SelectionRequest, WorldCmInt2>>();
            engine.GlobalContext[EntitySelectionCallbacksKey] = selectionCallbacks;
            engine.GlobalContext[SelectionTriggeredCallbacksKey] = triggeredCallbacks;

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

            var selectionRules = (SelectionRuleRegistry)engine.GlobalContext[CoreServiceKeys.SelectionRuleRegistry.Name];

            var clickSelect = new EntityClickSelectSystem(engine.World, engine.GlobalContext, engine.SpatialQueries);
            clickSelect.OnEntitySelected = (worldCm, entity) =>
            {
                foreach (var cb in selectionCallbacks) cb(worldCm, entity);
            };
            engine.RegisterSystem(clickSelect, SystemGroup.InputCollection);

            var gasSelection = new GasSelectionResponseSystem(engine.World, engine.GlobalContext, engine.SpatialQueries, selectionRules);
            gasSelection.OnSelectionTriggered = (req, worldCm) =>
            {
                foreach (var cb in triggeredCallbacks) cb(req, worldCm);
            };
            engine.RegisterSystem(gasSelection, SystemGroup.InputCollection);

            engine.RegisterSystem(new GasInputResponseSystem(engine.World, engine.GlobalContext), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new SkillBarOverlaySystem(engine.World, engine.GlobalContext));
            engine.RegisterPresentationSystem(new SelectionBoxOverlaySystem(engine.World, engine.GlobalContext));
            engine.RegisterSystem(new TabTargetCycleSystem(engine.World, engine.GlobalContext, engine.SpatialQueries), SystemGroup.InputCollection);

            var vmManager = new ViewModeManager(engine.World, engine.GlobalContext, engine.GameSession.Camera);
            engine.GlobalContext[ViewModeManager.GlobalKey] = vmManager;
            engine.RegisterSystem(new ViewModeSwitchSystem(engine.GlobalContext), SystemGroup.InputCollection);

            _ctx.Log("[CoreInputMod] EntityClickSelect, GasSelectionResponse, GasInputResponse, SkillBar, SelectionBox, TabTarget, ViewMode registered");
            return Task.CompletedTask;
        }
    }
}
