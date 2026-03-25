using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Input.Selection;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;

namespace RtsDemoMod.Systems
{
    public sealed class RtsSelectionCommandPanelSystem : ISystem<float>
    {
        private const string GasSourceId = "gas.ability-slots";
        private const string PanelInstanceKey = "rts.selection.focus";

        private readonly GameEngine _engine;
        private EntityCommandPanelHandle _handle = EntityCommandPanelHandle.Invalid;
        private Entity _lastTarget = Entity.Null;

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

            if (!IsRtsMapActive())
            {
                ClosePanel(service);
                return;
            }

            EnsureSelectionViewBinding();

            Entity selected = SelectionContextRuntime.TryGetCurrentPrimary(_engine.World, _engine.GlobalContext, out Entity current)
                ? current
                : FindFallbackTarget();
            if (!IsPanelTarget(selected))
            {
                if (_handle.IsValid)
                {
                    service.SetVisible(_handle, visible: false);
                }
                _lastTarget = Entity.Null;
                return;
            }

            var anchor = new EntityCommandPanelAnchor(EntityCommandPanelAnchorPreset.BottomCenter, 0f, 20f);
            var size = new EntityCommandPanelSize(520f, 352f);

            if (!_handle.IsValid || !service.TryGetState(_handle, out _))
            {
                _handle = service.Open(new EntityCommandPanelOpenRequest
                {
                    TargetEntity = selected,
                    SourceId = GasSourceId,
                    InstanceKey = PanelInstanceKey,
                    Anchor = anchor,
                    Size = size,
                    InitialGroupIndex = 0,
                    StartVisible = true
                });
                _lastTarget = selected;
                return;
            }

            service.SetAnchor(_handle, in anchor);
            service.SetSize(_handle, in size);
            if (_lastTarget != selected)
            {
                service.RebindTarget(_handle, selected);
                service.SetGroupIndex(_handle, 0);
                _lastTarget = selected;
            }

            service.SetVisible(_handle, visible: true);
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

        private void EnsureSelectionViewBinding()
        {
            SelectionRuntime? selection = _engine.GetService(CoreServiceKeys.SelectionRuntime);
            Entity owner = _engine.GetService(CoreServiceKeys.LocalPlayerEntity);
            if (selection == null || !_engine.World.IsAlive(owner))
            {
                return;
            }

            selection.TryBindView(owner, SelectionViewKeys.Primary, owner, SelectionSetKeys.Ambient);
            _engine.GlobalContext[CoreServiceKeys.SelectionViewViewerEntity.Name] = owner;
            _engine.GlobalContext[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary;
        }

        private Entity FindFallbackTarget()
        {
            Entity result = FindFirstByNameContains("Peasant");
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

            result = FindFirstByNameContains("Gateway");
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
            if (_handle.IsValid)
            {
                service.Close(_handle);
                _handle = EntityCommandPanelHandle.Invalid;
            }

            _lastTarget = Entity.Null;
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
