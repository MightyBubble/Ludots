using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Arch.Core;
using CoreInputMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Systems;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.EntityCollections;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Client;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CoreInputMod.Triggers
{
    /// <summary>
    /// Registers generic input systems on game start.
    /// Does not include order sources (move/attack/etc) — those are game-mode specific (MobaDemoMod, RtsDemoMod, etc).
    /// For camera, compose CameraProfilesMod / CameraBootstrapMod / VirtualCameraShotsMod as needed.
    /// </summary>
    public sealed class InstallCoreInputOnGameStartTrigger : Trigger
    {
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

            _ = engine.GetService(CoreServiceKeys.InteractionActionBindings)
                ?? throw new InvalidOperationException("InteractionActionBindings must be registered before CoreInputMod installs.");

            _ = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore must be registered before CoreInputMod installs.");

            engine.RegisterSystem(new AbilityExecAimSyncSystem(engine.World, new InputInteractionContextAccessor(engine.World, engine.GlobalContext)), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new SkillBarOverlaySystem(
                engine.World,
                engine.GlobalContext,
                (out Entity owner) => TryResolveLocalCommandSourceOwner(engine, out owner)));
            _ctx.Log("[CoreInputMod] SkillBar registered");
            return Task.CompletedTask;
        }

        private static bool TryResolveLocalCommandSourceOwner(GameEngine engine, out Entity owner)
        {
            owner = Entity.Null;
            if (!ClientLocalSeatAccess.TryGetSolePossessedRep(engine, out Entity local) ||
                local == Entity.Null ||
                !engine.World.IsAlive(local))
            {
                return false;
            }

            owner = local;
            return true;
        }
    }
}
