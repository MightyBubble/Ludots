using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
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

            Entity selected = SelectionContextRuntime.TryGetCurrentPrimary(_engine.World, _engine.GlobalContext, out Entity current)
                ? current
                : Entity.Null;
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
            var size = new EntityCommandPanelSize(460f, 268f);

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
