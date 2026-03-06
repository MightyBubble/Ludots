using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CoreInputMod.Systems
{
    /// <summary>
    /// Shared helper for mod-level order source systems.
    /// Encapsulates the standard Input→Order wiring that all game modes share:
    /// tag resolution, ground raycast, entity providers, order submission.
    /// Each mod creates one instance and calls <see cref="TryCreateMapping"/>
    /// to get a fully-wired <see cref="InputOrderMappingSystem"/>.
    /// </summary>
    public sealed class LocalOrderSourceHelper
    {
        public const string ActiveMappingKey = "CoreInputMod.ActiveInputOrderMapping";

        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly OrderQueue _orders;

        public int CastAbilityTagId { get; }
        public int StopTagId { get; }

        public LocalOrderSourceHelper(World world, Dictionary<string, object> globals, OrderQueue orders)
        {
            _world = world;
            _globals = globals;
            _orders = orders;
            if (globals.TryGetValue(CoreServiceKeys.GameConfig.Name, out var c) && c is GameConfig cfg)
            {
                CastAbilityTagId = cfg.Constants.OrderTags["castAbility"];
                StopTagId = cfg.Constants.OrderTags["stop"];
            }
        }

        /// <summary>
        /// Creates a fully-wired InputOrderMappingSystem from the mod's VFS config.
        /// Returns null if InputHandler is not yet available.
        /// </summary>
        public InputOrderMappingSystem? TryCreateMapping(IModContext ctx)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.InputHandler.Name, out var io) ||
                io is not PlayerInputHandler input)
                return null;

            using var stream = ctx.VFS.GetStream($"{ctx.ModId}:assets/Input/input_order_mappings.json");
            var config = InputOrderMappingLoader.LoadFromStream(stream);
            var mapping = new InputOrderMappingSystem(input, config);

            mapping.SetTagKeyResolver(k => k switch
            {
                "castAbility" => CastAbilityTagId,
                "stop" => StopTagId,
                _ => 0
            });
            mapping.SetGroundPositionProvider((out Vector3 w) =>
            {
                w = default;
                if (!TryGetGroundWorldCm(out var p)) return false;
                w = new Vector3(p.X, 0f, p.Y);
                return true;
            });
            mapping.SetSelectedEntityProvider((out Entity e) => TryGetEntity(CoreServiceKeys.SelectedEntity.Name, out e));
            mapping.SetHoveredEntityProvider((out Entity e) => TryGetEntity(CoreServiceKeys.HoveredEntity.Name, out e));
            mapping.SetOrderSubmitHandler((in Order o) => _orders.TryEnqueue(o));

            _globals[ActiveMappingKey] = mapping;
            return mapping;
        }

        public bool TryGetEntity(string key, out Entity e)
        {
            e = default;
            if (!_globals.TryGetValue(key, out var o) || o is not Entity v || !_world.IsAlive(v))
                return false;
            e = v;
            return true;
        }

        public bool TryGetGroundWorldCm(out WorldCmInt2 worldCm)
        {
            worldCm = default;
            if (!_globals.TryGetValue(CoreServiceKeys.ScreenRayProvider.Name, out var r) || r is not IScreenRayProvider rp) return false;
            if (!_globals.TryGetValue(CoreServiceKeys.InputHandler.Name, out var io) || io is not PlayerInputHandler input) return false;
            var ray = rp.GetRay(input.ReadAction<Vector2>("PointerPos"));
            return GroundRaycastUtil.TryGetGroundWorldCm(in ray, out worldCm);
        }

        /// <summary>
        /// Standard actor resolution: SelectedEntity (if owned by playerId) → LocalPlayerEntity.
        /// </summary>
        public Entity GetControlledActor(int playerId = 1)
        {
            if (_globals.TryGetValue(CoreServiceKeys.SelectedEntity.Name, out var s) &&
                s is Entity sel && _world.IsAlive(sel))
            {
                if (_world.TryGet(sel, out PlayerOwner owner) && owner.PlayerId == playerId)
                    return sel;
            }
            if (_globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var l) && l is Entity loc)
                return loc;
            return default;
        }
    }
}
