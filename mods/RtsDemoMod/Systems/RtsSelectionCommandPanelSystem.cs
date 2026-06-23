using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Input.Selection;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;
using RtsDemoMod.Runtime;

namespace RtsDemoMod.Systems
{
    public sealed class RtsSelectionCommandPanelSystem : ISystem<float>
    {
        private const string GasSourceId = "gas.ability-slots";
        private const string CommandDeckInstanceKey = "rts.selection.command";
        private const string OrderMonitorInstanceKey = "rts.selection.orders";

        private readonly GameEngine _engine;
        private EntityCommandPanelHandle _commandDeckHandle = EntityCommandPanelHandle.Invalid;
        private EntityCommandPanelHandle _orderMonitorHandle = EntityCommandPanelHandle.Invalid;
        private Entity _lastTarget = Entity.Null;
        private bool _seededDefaultSelection;

        public RtsSelectionCommandPanelSystem(GameEngine engine)
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

            if (RtsDemoRuntimeKeys.AreNativeCommandPanelsSuppressed(_engine))
            {
                ClosePanel(service);
                _seededDefaultSelection = false;
                return;
            }

            if (!IsRtsMapActive())
            {
                ClosePanel(service);
                _seededDefaultSelection = false;
                return;
            }

            RtsShowcaseSelectionHelper.EnsureSelectionViewBinding(_engine);

            Entity selected = SelectionContextRuntime.TryGetCurrentPrimary(_engine.World, _engine.GlobalContext, out Entity current)
                ? current
                : Entity.Null;
            if (!IsPanelTarget(selected) && !_seededDefaultSelection)
            {
                Entity fallback = FindFallbackTarget();
                if (IsPanelTarget(fallback) && RtsShowcaseSelectionHelper.TrySelectAndFocus(_engine, fallback, snapCamera: true))
                {
                    _seededDefaultSelection = true;
                    selected = fallback;
                }
            }

            if (!IsPanelTarget(selected))
            {
                SetVisible(service, _commandDeckHandle, visible: false);
                SetVisible(service, _orderMonitorHandle, visible: false);
                _lastTarget = Entity.Null;
                return;
            }

            var commandAnchor = new EntityCommandPanelAnchor(EntityCommandPanelAnchorPreset.BottomCenter, 0f, 18f);
            var commandSize = new EntityCommandPanelSize(702f, 226f);
            var monitorAnchor = new EntityCommandPanelAnchor(EntityCommandPanelAnchorPreset.TopRight, 28f, 126f);
            var monitorSize = new EntityCommandPanelSize(390f, 428f);

            EnsurePanel(
                service,
                ref _commandDeckHandle,
                selected,
                CommandDeckInstanceKey,
                commandAnchor,
                commandSize,
                EntityCommandPanelLayoutPreset.CommandDeck);
            EnsurePanel(
                service,
                ref _orderMonitorHandle,
                selected,
                OrderMonitorInstanceKey,
                monitorAnchor,
                monitorSize,
                EntityCommandPanelLayoutPreset.OrderMonitor);

            if (_lastTarget != selected)
            {
                service.RebindTarget(_commandDeckHandle, selected);
                service.RebindTarget(_orderMonitorHandle, selected);
                service.SetGroupIndex(_commandDeckHandle, 0);
                service.SetGroupIndex(_orderMonitorHandle, 0);
                _lastTarget = selected;
            }

            SetVisible(service, _commandDeckHandle, visible: true);
            SetVisible(service, _orderMonitorHandle, visible: true);
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
            return _engine.World.IsAlive(entity) && _engine.World.Has<AbilityStateBuffer>(entity);
        }

        private Entity FindFallbackTarget()
        {
            Entity result = FindFirstByNameContains("Barracks");
            if (result != Entity.Null)
            {
                return result;
            }

            result = FindFirstByNameContains("War Factory");
            if (result != Entity.Null)
            {
                return result;
            }

            result = FindFirstByNameContains("Gateway");
            if (result != Entity.Null)
            {
                return result;
            }

            result = FindFirstByNameContains("Peasant");
            if (result != Entity.Null)
            {
                return result;
            }

            result = FindFirstByNameContains("ConYard");
            if (result != Entity.Null)
            {
                return result;
            }

            result = FindFirstByNameContains("Construction Yard");
            if (result != Entity.Null)
            {
                return result;
            }

            result = FindFirstByNameContains("Drone");
            if (result != Entity.Null)
            {
                return result;
            }

            return FindFirstAbilityTarget();
        }

        private Entity FindFirstByNameContains(string nameToken)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            _engine.World.Query(in query, (Entity entity, ref Name name) =>
            {
                if (result == Entity.Null &&
                    !string.IsNullOrWhiteSpace(name.Value) &&
                    name.Value.IndexOf(nameToken, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result = entity;
                }
            });

            return result;
        }

        private Entity FindFirstAbilityTarget()
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name, AbilityStateBuffer>();
            _engine.World.Query(in query, (Entity entity, ref Name _, ref AbilityStateBuffer _) =>
            {
                if (result == Entity.Null)
                {
                    result = entity;
                }
            });

            return result;
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
            Entity selected,
            string instanceKey,
            in EntityCommandPanelAnchor anchor,
            in EntityCommandPanelSize size,
            EntityCommandPanelLayoutPreset layoutPreset)
        {
            if (!handle.IsValid || !service.TryGetState(handle, out _))
            {
                handle = service.Open(new EntityCommandPanelOpenRequest
                {
                    TargetEntity = selected,
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
