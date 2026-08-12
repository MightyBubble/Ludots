using System;
using System.Threading.Tasks;
using Arch.Core;
using CameraShowcaseMod.Input;
using CameraShowcaseMod.UI;
using CoreInputMod;
using CoreInputMod.ViewMode;
using Ludots.Core.Components;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Knowledge;
using Ludots.Core.Scripting;
using Ludots.Core.Modding;
using Ludots.UI.Surface;

namespace CameraShowcaseMod.Runtime
{
    internal sealed class CameraShowcaseRuntime
    {
        private const string CommandSourceTitle = "Camera showcase command source";
        private const string CommandSourceSummary = "Map-owned camera actors.";

        private readonly IModContext _context;
        private readonly CameraShowcasePanelController _panelController;
        private bool _inputContextActive;

        public CameraShowcaseRuntime(IModContext context)
        {
            _context = context;
            _panelController = new CameraShowcasePanelController();
        }

        public Task HandleMapFocusedAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            string? activeMapId = engine.CurrentMapSession?.MapId.Value;
            bool showcaseActive = CameraShowcaseIds.IsShowcaseMap(activeMapId);
            var viewModeManager = ResolveViewModeManager(engine);

            var input = context.Get(CoreServiceKeys.InputHandler);
            if (showcaseActive)
            {
                EnsureLocalCommandSourceOwner(engine, activeMapId, out Entity owner);
                ActivateInputContext(input);
                if (string.Equals(activeMapId, CameraShowcaseIds.CommandSourceFollowMapId, StringComparison.OrdinalIgnoreCase) &&
                    owner != Entity.Null)
                {
                    RequestCollectionFollowCamera(engine, CameraShowcaseIds.CommandSourceFollowProfileId, owner);
                }

                MountPanel(context, engine, activeMapId!, viewModeManager);
            }
            else
            {
                ClearCommandSourceFollowModeIfOwned(viewModeManager);
                DeactivateInputContext(input);
                ClearPanelIfOwned(context);
            }

            return Task.CompletedTask;
        }

        public Task HandleMapUnloadedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            var mapId = context.Get(CoreServiceKeys.MapId);
            if (string.IsNullOrWhiteSpace(mapId.Value) ||
                !CameraShowcaseIds.IsShowcaseMap(mapId.Value))
            {
                return Task.CompletedTask;
            }

            ClearCommandSourceFollowModeIfOwned(ResolveViewModeManager(engine));
            DeactivateInputContext(context.Get(CoreServiceKeys.InputHandler));
            ClearPanelIfOwned(context);
            return Task.CompletedTask;
        }

        private static bool EnsureLocalCommandSourceOwner(GameEngine engine, string? mapId, out Entity owner)
        {
            owner = Entity.Null;
            if (engine == null || !CameraShowcaseIds.IsShowcaseMap(mapId))
            {
                return false;
            }

            if (!TryFindEntityByName(engine.World, CameraShowcaseIds.HeroName, out Entity hero))
            {
                return false;
            }

            owner = hero;
            if (TryResolvePlayerId(engine.World, owner, out int playerId))
            {
                ClientLocalSeatBindings.BindSoleSeat(engine, owner, playerId);
            }
            else
            {
                ClientLocalSeatBindings.BindSoleSeat(engine, owner);
            }

            PublishEmptyCommandSourceCollection(engine, owner);
            PublishLocalKnowledge(engine, owner);
            return true;
        }

        private static bool TryResolvePlayerId(World world, Entity owner, out int playerId)
        {
            playerId = 0;
            if (owner == Entity.Null || !world.IsAlive(owner) || !world.Has<PlayerOwner>(owner))
            {
                return false;
            }

            playerId = world.Get<PlayerOwner>(owner).PlayerId;
            return playerId > 0;
        }

        private static void PublishEmptyCommandSourceCollection(GameEngine engine, Entity owner)
        {
            if (engine.GetService(CoreServiceKeys.EntityCollectionStore) is not EntityCollectionStore collections)
            {
                return;
            }

            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.CommandSource,
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.CommandSource,
                contextEntity: owner,
                primaryEntity: Entity.Null,
                title: CommandSourceTitle,
                summary: CommandSourceSummary);
            collections.Replace(owner, in descriptor, ReadOnlySpan<Entity>.Empty, owner);
        }

        private static void PublishLocalKnowledge(GameEngine engine, Entity owner)
        {
            if (engine.GetService(CoreServiceKeys.KnowledgeProjectionStore) is not KnowledgeProjectionStore knowledge)
            {
                return;
            }

            PublishLiveKnowledge(engine, knowledge, owner, owner);
            if (TryFindEntityByName(engine.World, CameraShowcaseIds.ScoutName, out Entity scout))
            {
                PublishLiveKnowledge(engine, knowledge, owner, scout);
            }

            if (TryFindEntityByName(engine.World, CameraShowcaseIds.CaptainName, out Entity captain))
            {
                PublishLiveKnowledge(engine, knowledge, owner, captain);
            }
        }

        private static void PublishLiveKnowledge(
            GameEngine engine,
            KnowledgeProjectionStore knowledge,
            Entity owner,
            Entity target)
        {
            if (owner == Entity.Null ||
                target == Entity.Null ||
                !engine.World.IsAlive(owner) ||
                !engine.World.IsAlive(target))
            {
                return;
            }

            var record = new KnowledgeDisclosureRecord(
                KnowledgePresence.LiveVisible,
                KnowledgePositionAccess.Live,
                KnowledgeIdMask256.Empty,
                KnowledgeIdMask256.Empty,
                KnowledgeIdMask256.Empty,
                owner,
                engine.GameSession?.CurrentTick ?? 0,
                expiryTick: 0,
                confidencePermille: 1000,
                revision: 0);
            knowledge.Upsert(owner, target, in record);
        }

        private static void RequestCollectionFollowCamera(GameEngine engine, string cameraId, Entity owner)
        {
            if (owner == Entity.Null ||
                !engine.World.IsAlive(owner) ||
                engine.GetService(CoreServiceKeys.VirtualCameraRegistry) is not VirtualCameraRegistry registry ||
                !registry.TryGet(cameraId, out var definition) ||
                definition == null)
            {
                return;
            }

            engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
            {
                Id = cameraId,
                BlendDurationSeconds = 0f,
                FollowTargetKindOverride = CameraFollowTargetKind.EntityCollectionPrimary,
                FollowCollectionOwnerOverride = owner,
                FollowCollectionKeyOverride = EntityCollectionKeys.CommandSource,
                SnapToFollowTargetWhenAvailable = definition.SnapToFollowTargetWhenAvailable,
                ResetRuntimeState = true,
                ReplaceActiveStack = true
            });
        }

        private static bool TryFindEntityByName(World world, string name, out Entity result)
        {
            result = Entity.Null;
            if (world == null || string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            Entity found = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name entityName) =>
            {
                if (found == Entity.Null &&
                    string.Equals(entityName.Value, name, StringComparison.OrdinalIgnoreCase))
                {
                    found = entity;
                }
            });
            result = found;
            return result != Entity.Null && world.IsAlive(result);
        }

        private void ActivateInputContext(PlayerInputHandler? input)
        {
            if (input == null || _inputContextActive)
            {
                return;
            }

            EnsureShowcaseInputSchema(input);
            input.PushContext(CameraShowcaseInputContexts.Showcase);
            _inputContextActive = true;
        }

        private void DeactivateInputContext(PlayerInputHandler? input)
        {
            if (input == null || !_inputContextActive)
            {
                return;
            }

            input.PopContext(CameraShowcaseInputContexts.Showcase);
            _inputContextActive = false;
        }

        private void MountPanel(ScriptContext context, GameEngine engine, string activeMapId, ViewModeManager? viewModeManager)
        {
            if (context.Get(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
            {
                return;
            }

            _panelController.PublishOrRefresh(engine, activeMapId, viewModeManager, surfaceHost);
        }

        private void ClearPanelIfOwned(ScriptContext context)
        {
            if (context.Get(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
            {
                return;
            }

            _panelController.ClearIfOwned(surfaceHost);
        }

        private static ViewModeManager? ResolveViewModeManager(GameEngine engine)
        {
            return CoreInputRuntimeServices.GetViewModeManager(engine);
        }

        private static void ClearCommandSourceFollowModeIfOwned(ViewModeManager? viewModeManager)
        {
            if (viewModeManager != null &&
                string.Equals(viewModeManager.ActiveMode?.Id, CameraShowcaseIds.CommandSourceFollowModeId, StringComparison.OrdinalIgnoreCase))
            {
                viewModeManager.ClearActiveMode();
            }
        }

        private static void EnsureShowcaseInputSchema(PlayerInputHandler input)
        {
            if (!input.HasContext(CameraShowcaseInputContexts.Showcase))
            {
                throw new InvalidOperationException($"Missing input context: {CameraShowcaseInputContexts.Showcase}");
            }

            if (!input.HasAction(CameraShowcaseIds.CommandSourceFollowModeActionId))
            {
                throw new InvalidOperationException($"Missing input action: {CameraShowcaseIds.CommandSourceFollowModeActionId}");
            }
        }
    }
}
