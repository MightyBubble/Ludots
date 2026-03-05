using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;

namespace CoreInputMod.Triggers
{
    /// <summary>
    /// Registers generic input systems on game start: EntityClickSelect, GasSelectionResponse, GasInputResponse.
    /// Does not include order sources (move/attack/etc) — those are game-mode specific (MobaDemoMod, RtsDemoMod, etc).
    /// For camera, add Universal3CCameraMod.
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

            var selectionCallbacks = new List<Action<WorldCmInt2, Arch.Core.Entity>>();
            var triggeredCallbacks = new List<Action<SelectionRequest, WorldCmInt2>>();
            engine.GlobalContext[EntitySelectionCallbacksKey] = selectionCallbacks;
            engine.GlobalContext[SelectionTriggeredCallbacksKey] = triggeredCallbacks;

            var clickSelect = new EntityClickSelectSystem(engine.World, engine.GlobalContext, engine.SpatialQueries);
            clickSelect.OnEntitySelected = (worldCm, entity) =>
            {
                foreach (var cb in selectionCallbacks) cb(worldCm, entity);
            };
            engine.RegisterPresentationSystem(clickSelect);

            var gasSelection = new GasSelectionResponseSystem(engine.World, engine.GlobalContext, engine.SpatialQueries);
            gasSelection.OnSelectionTriggered = (req, worldCm) =>
            {
                foreach (var cb in triggeredCallbacks) cb(req, worldCm);
            };
            engine.RegisterPresentationSystem(gasSelection);

            engine.RegisterPresentationSystem(new GasInputResponseSystem(engine.World, engine.GlobalContext));

            _ctx.Log("[CoreInputMod] EntityClickSelect, GasSelectionResponse, GasInputResponse registered");
            return Task.CompletedTask;
        }
    }
}
