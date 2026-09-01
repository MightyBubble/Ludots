using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Client;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;
using RtsDemoMod.Runtime;

namespace RtsDemoMod.Systems
{
    public sealed class RtsCommandSourceCommandPanelSystem : ISystem<float>
    {
        private const string GasSourceId = "gas.ability-slots";
        private const string CommandDeckInstanceKey = "rts.command_source.command";
        private const string OrderMonitorInstanceKey = "rts.command_source.orders";

        private static readonly QueryDescription SelectableCommandSourceQuery = new QueryDescription()
            .WithAll<MapEntity, PlayerOwner, AbilityStateBuffer, CommandSourceSelectableTag, CommandSourceSelectableState>();

        private readonly GameEngine _engine;
        private EntityCommandPanelHandle _commandDeckHandle = EntityCommandPanelHandle.Invalid;
        private EntityCommandPanelHandle _orderMonitorHandle = EntityCommandPanelHandle.Invalid;
        private Entity _lastTarget = Entity.Null;
        private MapConfig? _cachedMapConfig;
        private RtsCommandSourceUiMapConfig? _cachedUiConfig;

        public RtsCommandSourceCommandPanelSystem(GameEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            IEntityCommandPanelService? service = _engine.GetService(CoreServiceKeys.EntityCommandPanelService);
            if (service == null)
            {
                return;
            }

            if (!IsRtsMapActive())
            {
                ClosePanel(service);
                return;
            }

            RtsShowcaseCommandSourceHelper.EnsureCommandSourceBinding(_engine);

            Entity commandSource = ResolveCommandSource();

            if (!IsPanelTarget(commandSource))
            {
                SetVisible(service, _commandDeckHandle, visible: false);
                SetVisible(service, _orderMonitorHandle, visible: false);
                _lastTarget = Entity.Null;
                return;
            }

            RtsCommandSourceUiMapConfig uiConfig = ResolveUiConfig();

            EnsurePanel(
                service,
                ref _commandDeckHandle,
                commandSource,
                CommandDeckInstanceKey,
                uiConfig.CommandDeck,
                EntityCommandPanelLayoutPreset.CommandDeck);
            EnsurePanel(
                service,
                ref _orderMonitorHandle,
                commandSource,
                OrderMonitorInstanceKey,
                uiConfig.OrderMonitor,
                EntityCommandPanelLayoutPreset.OrderMonitor);

            if (_lastTarget != commandSource)
            {
                service.RebindTarget(_commandDeckHandle, commandSource);
                service.RebindTarget(_orderMonitorHandle, commandSource);
                service.SetGroupIndex(_commandDeckHandle, 0);
                service.SetGroupIndex(_orderMonitorHandle, 0);
                _lastTarget = commandSource;
            }

            SetVisible(service, _commandDeckHandle, uiConfig.CommandDeck.Visible);
            SetVisible(service, _orderMonitorHandle, uiConfig.OrderMonitor.Visible);
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
            IEntityCommandPanelService? service = _engine.GetService(CoreServiceKeys.EntityCommandPanelService);
            if (service != null)
            {
                ClosePanel(service);
            }
        }

        private bool IsPanelTarget(Entity entity)
        {
            return _engine.World.IsAlive(entity) &&
                   _engine.World.Has<AbilityStateBuffer>(entity) &&
                   _engine.World.Has<CommandSourceSelectableTag>(entity) &&
                   _engine.World.Has<CommandSourceSelectableState>(entity) &&
                   _engine.World.Get<CommandSourceSelectableState>(entity).Enabled;
        }

        private void ClosePanel(IEntityCommandPanelService service)
        {
            CloseHandle(service, ref _commandDeckHandle);
            CloseHandle(service, ref _orderMonitorHandle);

            _lastTarget = Entity.Null;
        }

        private static void CloseHandle(IEntityCommandPanelService service, ref EntityCommandPanelHandle handle)
        {
            if (!handle.IsValid)
            {
                return;
            }

            service.Close(handle);
            handle = EntityCommandPanelHandle.Invalid;
        }

        private static void SetVisible(IEntityCommandPanelService service, EntityCommandPanelHandle handle, bool visible)
        {
            if (handle.IsValid)
            {
                service.SetVisible(handle, visible);
            }
        }

        private static void EnsurePanel(
            IEntityCommandPanelService service,
            ref EntityCommandPanelHandle handle,
            Entity commandSource,
            string instanceKey,
            RtsCommandSourcePanelMapConfig config,
            EntityCommandPanelLayoutPreset layoutPreset)
        {
            EntityCommandPanelAnchor anchor = config.ToAnchor();
            EntityCommandPanelSize size = config.ToSize();
            if (!handle.IsValid || !service.TryGetState(handle, out _))
            {
                handle = service.Open(new EntityCommandPanelOpenRequest
                {
                    TargetEntity = commandSource,
                    SourceId = GasSourceId,
                    InstanceKey = instanceKey,
                    Anchor = anchor,
                    Size = size,
                    LayoutPreset = layoutPreset,
                    InitialGroupIndex = 0,
                    StartVisible = config.Visible
                });
                return;
            }

            service.SetAnchor(handle, in anchor);
            service.SetSize(handle, in size);
        }

        private Entity ResolveCommandSource()
        {
            if (RtsShowcaseCommandSourceHelper.TryGetCommandSourcePrimary(_engine, out Entity current) &&
                IsPanelTarget(current))
            {
                return current;
            }

            if (!TryFindFirstOwnedSelectable(out Entity candidate) ||
                !RtsShowcaseCommandSourceHelper.TrySetCommandSourceAndFocus(_engine, candidate, snapCamera: true))
            {
                return Entity.Null;
            }

            return candidate;
        }

        private bool TryFindFirstOwnedSelectable(out Entity result)
        {
            result = Entity.Null;
            MapSession? session = _engine.CurrentMapSession;
            if (session == null ||
                !ClientLocalSeatAccess.TryGetSolePossessedRep(_engine, out Entity localRep) ||
                !_engine.World.TryGet(localRep, out PlayerIdentity localIdentity))
            {
                return false;
            }

            int localPlayerId = localIdentity.PlayerId;
            foreach (ref Chunk chunk in _engine.World.Query(in SelectableCommandSourceQuery))
            {
                ReadOnlySpan<MapEntity> mapEntities = chunk.GetSpan<MapEntity>();
                ReadOnlySpan<PlayerOwner> owners = chunk.GetSpan<PlayerOwner>();
                ReadOnlySpan<CommandSourceSelectableState> states =
                    chunk.GetSpan<CommandSourceSelectableState>();
                ref Entity first = ref chunk.Entity(0);
                foreach (int index in chunk)
                {
                    if (mapEntities[index].MapId != session.MapId ||
                        owners[index].PlayerId != localPlayerId ||
                        !states[index].Enabled)
                    {
                        continue;
                    }

                    Entity candidate = Unsafe.Add(ref first, index);
                    if (result == Entity.Null || candidate.Id < result.Id)
                    {
                        result = candidate;
                    }
                }
            }

            return result != Entity.Null;
        }

        private RtsCommandSourceUiMapConfig ResolveUiConfig()
        {
            MapConfig? mapConfig = _engine.CurrentMapSession?.MapConfig;
            if (!ReferenceEquals(mapConfig, _cachedMapConfig) || _cachedUiConfig == null)
            {
                _cachedMapConfig = mapConfig;
                _cachedUiConfig = RtsCommandSourceUiMapConfig.Resolve(mapConfig);
            }

            return _cachedUiConfig;
        }

        private bool IsRtsMapActive()
        {
            var tags = _engine.CurrentMapSession?.MapConfig?.Tags;
            if (tags == null)
            {
                return false;
            }

            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], "rts", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tags[i], "rts_showcase", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
