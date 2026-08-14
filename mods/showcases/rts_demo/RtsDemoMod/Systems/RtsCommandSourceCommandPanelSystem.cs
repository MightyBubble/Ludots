using System;
using Arch.Core;
using Arch.System;
using CoreInputMod.Systems;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
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

        private readonly GameEngine _engine;
        private EntityCommandPanelHandle _commandDeckHandle = EntityCommandPanelHandle.Invalid;
        private EntityCommandPanelHandle _orderMonitorHandle = EntityCommandPanelHandle.Invalid;
        private Entity _lastTarget = Entity.Null;
        private bool _seededDefaultCommandSource;
        private int _lastLocalPlayerId;
        private MapConfig? _cachedMapConfig;
        private RtsCommandSourceUiMapConfig? _cachedUiConfig;
        private bool _skillBarVisibilityOwned;
        private bool _hadPreviousSkillBarVisibility;
        private bool _previousSkillBarVisibility;

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
                RestoreSkillBarVisibility();
                _seededDefaultCommandSource = false;
                return;
            }

            RtsShowcaseCommandSourceHelper.EnsureCommandSourceBinding(_engine);

            int localPlayerId = _engine.GetService(CoreServiceKeys.LocalPlayerId);
            if (_lastLocalPlayerId != localPlayerId)
            {
                _lastLocalPlayerId = localPlayerId;
                _seededDefaultCommandSource = false;
            }

            RtsCommandSourceUiMapConfig uiConfig = ResolveUiConfig();
            ApplySkillBarVisibility(uiConfig.SkillBarVisible);

            Entity commandSource = RtsShowcaseCommandSourceHelper.TryGetCommandSourcePrimary(_engine, out Entity current)
                ? current
                : Entity.Null;
            if (!IsPanelTarget(commandSource, localPlayerId) && !_seededDefaultCommandSource)
            {
                Entity fallback = FindFallbackTarget(localPlayerId);
                if (IsPanelTarget(fallback, localPlayerId) &&
                    RtsShowcaseCommandSourceHelper.TrySetCommandSourceAndFocus(_engine, fallback, snapCamera: true))
                {
                    _seededDefaultCommandSource = true;
                    commandSource = fallback;
                }
            }

            if (!IsPanelTarget(commandSource, localPlayerId))
            {
                SetVisible(service, _commandDeckHandle, visible: false);
                SetVisible(service, _orderMonitorHandle, visible: false);
                _lastTarget = Entity.Null;
                return;
            }

            EntityCommandPanelAnchor commandAnchor = uiConfig.CommandDeck.ToAnchor();
            EntityCommandPanelSize commandSize = uiConfig.CommandDeck.ToSize();
            EntityCommandPanelAnchor monitorAnchor = uiConfig.OrderMonitor.ToAnchor();
            EntityCommandPanelSize monitorSize = uiConfig.OrderMonitor.ToSize();

            EnsurePanel(
                service,
                ref _commandDeckHandle,
                commandSource,
                CommandDeckInstanceKey,
                commandAnchor,
                commandSize,
                EntityCommandPanelLayoutPreset.CommandDeck);
            EnsurePanel(
                service,
                ref _orderMonitorHandle,
                commandSource,
                OrderMonitorInstanceKey,
                monitorAnchor,
                monitorSize,
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
            RestoreSkillBarVisibility();
        }

        private bool IsPanelTarget(Entity entity, int localPlayerId)
        {
            return localPlayerId > 0 &&
                   _engine.World.IsAlive(entity) &&
                   _engine.World.Has<AbilityStateBuffer>(entity) &&
                   _engine.World.TryGet(entity, out PlayerOwner owner) &&
                   owner.PlayerId == localPlayerId;
        }

        private Entity FindFallbackTarget(int localPlayerId)
        {
            Entity result = FindFirstByNameContains("Peasant", localPlayerId);
            if (result != Entity.Null)
            {
                return result;
            }

            result = FindFirstByNameContains("Barracks", localPlayerId);
            if (result != Entity.Null)
            {
                return result;
            }

            result = FindFirstByNameContains("War Factory", localPlayerId);
            if (result != Entity.Null)
            {
                return result;
            }

            result = FindFirstByNameContains("Gateway", localPlayerId);
            if (result != Entity.Null)
            {
                return result;
            }

            result = FindFirstByNameContains("ConYard", localPlayerId);
            if (result != Entity.Null)
            {
                return result;
            }

            result = FindFirstByNameContains("Construction Yard", localPlayerId);
            if (result != Entity.Null)
            {
                return result;
            }

            result = FindFirstByNameContains("Drone", localPlayerId);
            if (result != Entity.Null)
            {
                return result;
            }

            return FindFirstAbilityTarget(localPlayerId);
        }

        private Entity FindFirstByNameContains(string nameToken, int localPlayerId)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name, PlayerOwner>();
            _engine.World.Query(in query, (Entity entity, ref Name name, ref PlayerOwner owner) =>
            {
                if (result == Entity.Null &&
                    owner.PlayerId == localPlayerId &&
                    !string.IsNullOrWhiteSpace(name.Value) &&
                    name.Value.IndexOf(nameToken, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result = entity;
                }
            });

            return result;
        }

        private Entity FindFirstAbilityTarget(int localPlayerId)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name, AbilityStateBuffer, PlayerOwner>();
            _engine.World.Query(in query, (Entity entity, ref Name _, ref AbilityStateBuffer _, ref PlayerOwner owner) =>
            {
                if (result == Entity.Null && owner.PlayerId == localPlayerId)
                {
                    result = entity;
                }
            });

            return result;
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

        private void ApplySkillBarVisibility(bool visible)
        {
            if (!_skillBarVisibilityOwned)
            {
                _skillBarVisibilityOwned = true;
                if (_engine.GlobalContext.TryGetValue(
                        SkillBarOverlaySystem.SkillBarEnabledKey,
                        out object? previous) &&
                    previous is bool previousVisibility)
                {
                    _hadPreviousSkillBarVisibility = true;
                    _previousSkillBarVisibility = previousVisibility;
                }
            }

            _engine.GlobalContext[SkillBarOverlaySystem.SkillBarEnabledKey] = visible;
        }

        private void RestoreSkillBarVisibility()
        {
            if (!_skillBarVisibilityOwned)
            {
                return;
            }

            if (_hadPreviousSkillBarVisibility)
            {
                _engine.GlobalContext[SkillBarOverlaySystem.SkillBarEnabledKey] = _previousSkillBarVisibility;
            }
            else
            {
                _engine.GlobalContext.Remove(SkillBarOverlaySystem.SkillBarEnabledKey);
            }

            _skillBarVisibilityOwned = false;
            _hadPreviousSkillBarVisibility = false;
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
            in EntityCommandPanelAnchor anchor,
            in EntityCommandPanelSize size,
            EntityCommandPanelLayoutPreset layoutPreset)
        {
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
                    StartVisible = true
                });
                return;
            }

            service.SetAnchor(handle, in anchor);
            service.SetSize(handle, in size);
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
