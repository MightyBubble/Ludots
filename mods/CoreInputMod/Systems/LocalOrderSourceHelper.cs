using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CoreInputMod.Systems
{
    public sealed class LocalOrderSourceHelper
    {
        public const string ActiveMappingKey = "CoreInputMod.ActiveInputOrderMapping";

        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly OrderQueue _orders;

        public int CastAbilityOrderTagId { get; }
        public int StopOrderTagId { get; }

        public LocalOrderSourceHelper(World world, Dictionary<string, object> globals, OrderQueue orders)
        {
            _world = world;
            _globals = globals;
            _orders = orders;
            if (globals.TryGetValue(CoreServiceKeys.GameConfig.Name, out var configObj) && configObj is GameConfig config)
            {
                CastAbilityOrderTagId = config.Constants.OrderTags["castAbility"];
                StopOrderTagId = config.Constants.OrderTags["stop"];
            }
        }

        public InputOrderMappingSystem? TryCreateMapping(IModContext ctx)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.InputHandler.Name, out var inputObj) || inputObj is not PlayerInputHandler input)
            {
                return null;
            }

            using var stream = ctx.VFS.GetStream($"{ctx.ModId}:assets/Input/input_order_mappings.json");
            var config = InputOrderMappingLoader.LoadFromStream(stream);
            var mapping = new InputOrderMappingSystem(input, config);

            mapping.SetTagKeyResolver(key => key switch
            {
                "castAbility" => CastAbilityOrderTagId,
                "stop" => StopOrderTagId,
                _ => 0
            });
            mapping.SetGroundPositionProvider((out Vector3 worldCm) =>
            {
                worldCm = default;
                if (!TryGetGroundWorldCm(out var point))
                {
                    return false;
                }

                worldCm = new Vector3(point.X, 0f, point.Y);
                return true;
            });
            mapping.SetSelectedEntityProvider((out Entity entity) => TryGetEntity(CoreServiceKeys.SelectedEntity.Name, out entity));
            mapping.SetHoveredEntityProvider((out Entity entity) => TryGetEntity(CoreServiceKeys.HoveredEntity.Name, out entity));
            if (_globals.TryGetValue(CoreServiceKeys.InteractionActionBindings.Name, out var bindingsObj) && bindingsObj is InteractionActionBindings bindings)
            {
                mapping.ConfirmActionId = bindings.ConfirmActionId;
                mapping.CancelActionId = bindings.CancelActionId;
            }
            mapping.SetOrderSubmitHandler((in Order order) => _orders.TryEnqueue(order));

            _globals[ActiveMappingKey] = mapping;
            return mapping;
        }

        public bool TryGetEntity(string key, out Entity entity)
        {
            entity = default;
            if (!_globals.TryGetValue(key, out var value) || value is not Entity candidate || !_world.IsAlive(candidate))
            {
                return false;
            }

            entity = candidate;
            return true;
        }

        public bool TryGetGroundWorldCm(out WorldCmInt2 worldCm)
        {
            worldCm = default;
            if (!_globals.TryGetValue(CoreServiceKeys.ScreenRayProvider.Name, out var rayProviderObj) || rayProviderObj is not IScreenRayProvider rayProvider)
            {
                return false;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.InputHandler.Name, out var inputObj) || inputObj is not PlayerInputHandler input)
            {
                return false;
            }

            var ray = rayProvider.GetRay(input.ReadAction<Vector2>("PointerPos"));
            return GroundRaycastUtil.TryGetGroundWorldCm(in ray, out worldCm);
        }

        public Entity GetControlledActor(int playerId = 1)
        {
            if (_globals.TryGetValue(CoreServiceKeys.SelectedEntity.Name, out var selectedObj) &&
                selectedObj is Entity selected &&
                _world.IsAlive(selected) &&
                _world.TryGet(selected, out PlayerOwner owner) &&
                owner.PlayerId == playerId)
            {
                return selected;
            }

            if (_globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var localObj) && localObj is Entity local)
            {
                return local;
            }

            return default;
        }
    }
}
