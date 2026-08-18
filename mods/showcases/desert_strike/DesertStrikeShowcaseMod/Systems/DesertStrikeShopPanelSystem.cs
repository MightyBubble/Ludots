using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;
using DesertStrikeShowcaseMod.Runtime;

namespace DesertStrikeShowcaseMod.Systems
{
    public sealed class DesertStrikeShopPanelSystem : ISystem<float>
    {
        private const string GasSourceId = "gas.ability-slots";
        private const string ShopInstanceKey = "desert_strike.shop";

        private readonly GameEngine _engine;
        private readonly DesertStrikeState _state;
        private EntityCommandPanelHandle _shopHandle = EntityCommandPanelHandle.Invalid;
        private Entity _lastTarget = Entity.Null;

        public DesertStrikeShopPanelSystem(GameEngine engine, DesertStrikeState state)
        {
            _engine = engine;
            _state = state;
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

            if (!IsDesertStrikeMapActive())
            {
                ClosePanel(service);
                _state.ShopPanelVisible = false;
                return;
            }

            Entity commandSource = TryGetCommandSourcePrimary();
            if (!IsPanelTarget(commandSource))
            {
                SetVisible(service, _shopHandle, visible: false);
                _lastTarget = Entity.Null;
                _state.ShopPanelVisible = false;
                return;
            }

            var anchor = new EntityCommandPanelAnchor(EntityCommandPanelAnchorPreset.BottomCenter, 0f, 18f);
            var size = new EntityCommandPanelSize(702f, 226f);
            EnsurePanel(service, commandSource, anchor, size);

            if (_lastTarget != commandSource)
            {
                service.RebindTarget(_shopHandle, commandSource);
                service.SetGroupIndex(_shopHandle, 0);
                _lastTarget = commandSource;
            }

            SetVisible(service, _shopHandle, visible: true);
            _state.ShopPanelVisible = true;
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

        private Entity TryGetCommandSourcePrimary()
        {
            Entity owner = Ludots.Core.Client.ClientLocalSeatAccess.RequireSolePossessedRep(_engine);
            return EntityCollectionContextRuntime.TryGetPrimary(
                       _engine.World,
                       _engine.GlobalContext,
                       owner,
                       Ludots.Core.EntityCollections.EntityCollectionKeys.CommandSource,
                       out Entity primary)
                ? primary
                : Entity.Null;
        }

        private void EnsurePanel(IEntityCommandPanelService service, Entity commandSource, in EntityCommandPanelAnchor anchor, in EntityCommandPanelSize size)
        {
            if (!_shopHandle.IsValid || !service.TryGetState(_shopHandle, out _))
            {
                _shopHandle = service.Open(new EntityCommandPanelOpenRequest
                {
                    TargetEntity = commandSource,
                    SourceId = GasSourceId,
                    InstanceKey = ShopInstanceKey,
                    Anchor = anchor,
                    Size = size,
                    LayoutPreset = EntityCommandPanelLayoutPreset.CommandDeck,
                    InitialGroupIndex = 0,
                    StartVisible = true,
                });
                return;
            }

            service.SetAnchor(_shopHandle, in anchor);
            service.SetSize(_shopHandle, in size);
        }

        private void ClosePanel(IEntityCommandPanelService service)
        {
            if (_shopHandle.IsValid)
            {
                service.Close(_shopHandle);
                _shopHandle = EntityCommandPanelHandle.Invalid;
            }

            _lastTarget = Entity.Null;
        }

        private static void SetVisible(IEntityCommandPanelService service, EntityCommandPanelHandle handle, bool visible)
        {
            if (handle.IsValid)
            {
                service.SetVisible(handle, visible);
            }
        }

        private bool IsDesertStrikeMapActive()
        {
            var tags = _engine.CurrentMapSession?.MapConfig?.Tags;
            if (tags == null)
            {
                return false;
            }

            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], "desert_strike", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
