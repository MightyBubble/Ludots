using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Arch.Core;
using CoreInputMod.Systems;
using CoreInputMod.ViewMode;
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

            engine.SetService(
                CoreServiceKeys.MinimapFocusCollectionProvider,
                (Ludots.Core.Presentation.Minimap.MinimapFocusCollectionProvider)TryResolveMinimapFocusCollection);

            // The acquisition system that fired these retired with the input→order graph line;
            // the registration point stays so dependent mods (camera follow, VFX hooks) can
            // attach, and the graph selection commit path can invoke them once it lands.
            if (!engine.TryGetService(CoreInputServiceKeys.CommandSourceAcquiredCallbacks, out var _))
            {
                engine.SetService(
                    CoreInputServiceKeys.CommandSourceAcquiredCallbacks,
                    new System.Collections.Generic.List<System.Action<Ludots.Platform.Abstractions.WorldCmInt2, Arch.Core.Entity>>());
            }

            _ = engine.GetService(CoreServiceKeys.InteractionActionBindings)
                ?? throw new InvalidOperationException("InteractionActionBindings must be registered before CoreInputMod installs.");

            _ = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore must be registered before CoreInputMod installs.");

            engine.RegisterSystem(new GasInputResponseSystem(engine.World, engine.GlobalContext), SystemGroup.InputCollection);
            engine.RegisterSystem(new AbilityExecAimSyncSystem(engine.World, new InputInteractionContextAccessor(engine.World, engine.GlobalContext)), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new SkillBarOverlaySystem(
                engine.World,
                engine.GlobalContext,
                (out Entity owner) => TryResolveLocalCommandSourceOwner(engine, out owner)));
            engine.InsertPresentationSystemBefore<EntityCollectionPresentationEventSystem>(new AbilityAimPresentationProjectionSystem(engine.World, engine.GlobalContext));
            engine.InsertPresentationSystemBefore<PresenterRuleSystem>(new CommandActorMovePathPresentationSystem(
                engine.World,
                engine.GlobalContext,
                (out Entity owner) => TryResolveLocalCommandSourceOwner(engine, out owner)));
            engine.RegisterSystem(new TabTargetCycleSystem(engine.World, engine.GlobalContext), SystemGroup.LocalInput);

            var vmManager = new ViewModeManager(engine.World, engine.GlobalContext);
            engine.SetService(CoreInputServiceKeys.ViewModeManager, vmManager);
            RegisterLoadedModViewModes(engine);
            engine.RegisterSystem(new ViewModeSwitchSystem(engine.GlobalContext), SystemGroup.LocalInput);
            RegisterAutoLocalOrderSource(engine);

_ctx.Log("[CoreInputMod] GasInputResponse, SkillBar, AbilityAimPresentation, CommandActorMovePathPresentation, TabTarget, ViewMode registered");

InstallDeclaredLocalOrderSources(engine);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Standard local order sources are config-declared: every loaded mod shipping
        /// assets/Input/local_order_source.json gets LocalOrderSourceSystem installed against its
        /// own input_order_mappings.json, replacing per-mod wrapper systems. Local order sources
        /// exist only where local presentation exists; authoritative servers skip them.
        /// </summary>
        private void InstallDeclaredLocalOrderSources(GameEngine engine)
        {
            if (engine.ModLoader?.LoadedModIds == null ||
                engine.GetService(CoreServiceKeys.NetworkProcessRole) == NetworkProcessRole.AuthoritativeServer)
            {
                return;
            }

            OrderQueue orders = engine.GetService(CoreServiceKeys.OrderQueue)
                ?? throw new InvalidOperationException("Declared local order sources require OrderQueue.");
            IReadOnlyList<string> modIds = engine.ModLoader.LoadedModIds;
            for (int i = 0; i < modIds.Count; i++)
            {
                string modId = modIds[i];
                string uri = $"{modId}:assets/Input/local_order_source.json";
                if (!_ctx.VFS.TryResolveFullPath(uri, out string? fullPath) || !File.Exists(fullPath))
                {
                    continue;
                }

                LocalOrderSourceConfig config;
                using (var stream = File.OpenRead(fullPath))
                {
                    config = LocalOrderSourceConfig.LoadFromStream(stream);
                }

                var group = string.Equals(config.SystemGroup, "LocalInput", StringComparison.Ordinal)
                    ? SystemGroup.LocalInput
                    : SystemGroup.InputCollection;
                engine.RegisterSystem(
                    new LocalOrderSourceSystem(engine.World, engine.GlobalContext, orders, _ctx, config, modId),
                    group);
                _ctx.Log($"[CoreInputMod] Installed declared local order source for {modId} ({config.SystemGroup}).");
            }
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

        private static bool TryResolveMinimapFocusCollection(GameEngine engine, out Entity owner, out string collectionKey)
        {
            bool found = TryResolveLocalCommandSourceOwner(engine, out owner);
            collectionKey = InputInteractionContextAccessor.CommandActorCollectionKey;
            return found;
        }

        /// <summary>
        /// Slice-2 auto assembly: exactly one loaded mod may ship the local order mapping
        /// config; the shipping mod is resolved here (load-time, fail-fast on ambiguity) and
        /// the shared config-installed order source replaces every per-mod installer.
        /// </summary>
        private void RegisterAutoLocalOrderSource(GameEngine engine)
        {
            string? sourceModId = null;
            var loadedModIds = engine.ModLoader?.LoadedModIds;
            if (loadedModIds != null)
            {
                for (int i = 0; i < loadedModIds.Count; i++)
                {
                    string modId = loadedModIds[i];
                    string uri = $"{modId}:assets/Input/input_order_mappings.json";
                    if (_ctx.VFS.TryResolveFullPath(uri, out string? path) && System.IO.File.Exists(path))
                    {
                        if (sourceModId != null)
                        {
                            throw new InvalidOperationException(
                                $"[CoreInputMod] Both '{sourceModId}' and '{modId}' ship assets/Input/input_order_mappings.json; " +
                                "exactly one gameplay mod may own the local order mapping per game set.");
                        }

                        sourceModId = modId;
                    }
                }
            }

            if (sourceModId == null)
            {
                _ctx.Log("[CoreInputMod] No loaded mod ships input_order_mappings.json; auto local order source stays uninstalled.");
                return;
            }

            // A mod that ships local_order_source.json gets its mapping installed by the declared
            // path below; auto-installing it too mounts a second mapping on the same actions and
            // every press submits twice (fireball double mana cost).
            string declaredUri = $"{sourceModId}:assets/Input/local_order_source.json";
            if (_ctx.VFS.TryResolveFullPath(declaredUri, out string? declaredPath) && System.IO.File.Exists(declaredPath))
            {
                _ctx.Log($"[CoreInputMod] '{sourceModId}' declares its own local order source; auto install skipped.");
                return;
            }

            OrderQueue orders = engine.GetService(CoreServiceKeys.OrderQueue)
                ?? throw new InvalidOperationException("[CoreInputMod] Auto local order source requires OrderQueue.");
            var autoOrderSource = new AutoInstalledLocalOrderSourceSystem(engine.World, engine.GlobalContext, orders, _ctx, sourceModId);
            engine.SetService(AutoInstalledLocalOrderSourceSystem.ServiceKey, autoOrderSource);
            engine.RegisterSystem(autoOrderSource, SystemGroup.InputCollection);
            _ctx.Log($"[CoreInputMod] Auto local order source installed from '{sourceModId}'.");
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
