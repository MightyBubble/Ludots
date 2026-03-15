using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CoreInputMod.Systems
{
    public sealed class LocalOrderSourceHelper
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly OrderQueue _orders;
        private readonly InputInteractionContextAccessor _context;

        public int CastAbilityOrderTypeId { get; }
        public int MoveToOrderTypeId { get; }
        public int StopOrderTypeId { get; }

        public LocalOrderSourceHelper(World world, Dictionary<string, object> globals, OrderQueue orders)
        {
            _world = world;
            _globals = globals;
            _orders = orders;
            _context = new InputInteractionContextAccessor(world, globals);
            if (globals.TryGetValue(CoreServiceKeys.GameConfig.Name, out var configObj) && configObj is GameConfig config)
            {
                CastAbilityOrderTypeId = config.Constants.OrderTypeIds["castAbility"];
                MoveToOrderTypeId = config.Constants.OrderTypeIds["moveTo"];
                StopOrderTypeId = config.Constants.OrderTypeIds["stop"];
            }
        }

        public InputOrderMappingSystem? TryCreateMapping(IModContext ctx)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) || inputObj is not IInputActionReader input)
            {
                return null;
            }

            string uri = $"{ctx.ModId}:assets/Input/input_order_mappings.json";
            if (!ctx.VFS.TryResolveFullPath(uri, out var fullPath) || !File.Exists(fullPath))
            {
                ctx.Log($"[{ctx.ModId}] input_order_mappings.json not found, skipping local order mapping.");
                return null;
            }

            using var stream = File.OpenRead(fullPath);
            var config = InputOrderMappingLoader.LoadFromStream(stream);
            var mapping = new InputOrderMappingSystem(input, config);

            mapping.SetOrderTypeKeyResolver(key => key switch
            {
                "castAbility" => CastAbilityOrderTypeId,
                "moveTo" => MoveToOrderTypeId,
                "stop" => StopOrderTypeId,
                _ => 0
            });
            mapping.SetGroundPositionProvider((out Vector3 worldCm) =>
            {
                worldCm = default;
                if (!_context.TryGetGroundWorldCm(out var point))
                {
                    return false;
                }

                worldCm = new Vector3(point.X, 0f, point.Y);
                return true;
            });
            mapping.SetActorProvider((out Entity entity) =>
            {
                entity = _context.GetControlledActor();
                return _world.IsAlive(entity);
            });
            mapping.SetSelectedEntityProvider((string setKey, out Entity entity) => _context.TryGetSelectedEntity(setKey, out entity));
            mapping.SetSelectedEntitiesProvider((string setKey, ref OrderEntitySelection entities) => _context.TryGetSelectedEntities(setKey, ref entities));
            mapping.SetHoveredEntityProvider((out Entity entity) => _context.TryGetEntity(CoreServiceKeys.HoveredEntity.Name, out entity));
            if (_globals.TryGetValue(CoreServiceKeys.InteractionActionBindings.Name, out var bindingsObj) && bindingsObj is InteractionActionBindings bindings)
            {
                mapping.ConfirmActionId = bindings.ConfirmActionId;
                mapping.CancelActionId = bindings.CancelActionId;
                mapping.CommandActionId = bindings.CommandActionId;
            }
            mapping.SetOrderSubmitHandler((in Order order) => _orders.TryEnqueue(order));
            if (TryCreateContextScoredResolver(out var contextResolver))
            {
                mapping.SetContextScoredProvider(contextResolver.TryResolve);
            }

            _globals[CoreServiceKeys.ActiveInputOrderMapping.Name] = mapping;
            return mapping;
        }

        public Entity GetControlledActor(int playerId = 1)
        {
            return _context.GetControlledActor(playerId);
        }

        private bool TryCreateContextScoredResolver(out ContextScoredOrderResolver resolver)
        {
            resolver = default!;
            if (!_globals.TryGetValue(CoreServiceKeys.ContextGroupRegistry.Name, out var groupsObj) ||
                groupsObj is not ContextGroupRegistry contextGroups ||
                !_globals.TryGetValue(CoreServiceKeys.GraphProgramRegistry.Name, out var graphsObj) ||
                graphsObj is not Ludots.Core.GraphRuntime.GraphProgramRegistry graphPrograms ||
                !_globals.TryGetValue(CoreServiceKeys.SpatialQueryService.Name, out var spatialObj) ||
                spatialObj is not Ludots.Core.Spatial.ISpatialQueryService spatialQueries ||
                !_globals.TryGetValue(CoreServiceKeys.SpatialCoordinateConverter.Name, out var coordsObj) ||
                coordsObj is not Ludots.Core.Spatial.ISpatialCoordinateConverter spatialCoords)
            {
                return false;
            }

            var graphApi = new GasGraphRuntimeApi(_world, spatialQueries, spatialCoords, eventBus: null, effectRequests: null);
            resolver = new ContextScoredOrderResolver(_world, contextGroups, graphPrograms, spatialQueries, graphApi);
            return true;
        }

    }
}
