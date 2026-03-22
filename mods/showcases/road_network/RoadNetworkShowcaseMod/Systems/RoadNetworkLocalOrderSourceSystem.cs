using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using CoreInputMod.Systems;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Primitives;
using Ludots.Core.Scripting;
using RoadNetworkShowcaseMod.Gameplay;

namespace RoadNetworkShowcaseMod.Systems
{
    internal sealed class RoadNetworkLocalOrderSourceSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly LocalOrderSourceHelper _helper;
        private readonly IModContext _context;
        private readonly RoadMoveOrderExpander _expander;
        private InputOrderMappingSystem? _mapping;
        private PresentationCommandBuffer? _presentationCommands;
        private PrefabRegistry? _prefabs;
        private int _cueMarkerPrefabId;
        private bool _initialized;

        public RoadNetworkLocalOrderSourceSystem(World world, Dictionary<string, object> globals, OrderQueue orders, IModContext context)
        {
            _world = world;
            _globals = globals;
            _context = context;
            _helper = new LocalOrderSourceHelper(world, globals, orders);
            _expander = new RoadMoveOrderExpander(world, globals, orders, RoadNetworkShowcaseIds.PathPlannerAgentTypeId);
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            EnsureInitialized();
            if (_mapping == null)
            {
                return;
            }

            Entity actor = _helper.GetControlledActor();
            if (!_world.IsAlive(actor))
            {
                return;
            }

            _mapping.SetLocalPlayer(actor, 1);
            _mapping.Update(dt);
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _mapping = _helper.TryCreateMapping(_context);
            _presentationCommands = _globals.TryGetValue(CoreServiceKeys.PresentationCommandBuffer.Name, out var commandObj) &&
                                    commandObj is PresentationCommandBuffer presentationCommands
                ? presentationCommands
                : null;
            _prefabs = _globals.TryGetValue(CoreServiceKeys.PresentationPrefabRegistry.Name, out var prefabObj) &&
                       prefabObj is PrefabRegistry prefabs
                ? prefabs
                : null;
            if (_mapping == null)
            {
                return;
            }

            _mapping.SetOrderSubmitHandler((in Order order) =>
            {
                _globals[LocalOrderSourceHelper.LastOrderDebugKey] =
                    $"type:{order.OrderTypeId},player:{order.PlayerId},actor:{order.Actor.Id}:{order.Actor.WorldId}:{order.Actor.Version},submit:{order.SubmitMode}";
                bool accepted = _expander.TrySubmit(in order);
                EmitSubmitCue(in order, accepted);
            });
            _mapping.SetQueueModifierProvider(() =>
            {
                return _globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) &&
                       inputObj is IInputActionReader input &&
                       input.IsDown("QueueModifier");
            });
        }

        private void EmitSubmitCue(in Order order, bool accepted)
        {
            if (_presentationCommands == null ||
                !OrderWorldSpatialResolver.TryResolveMoveDestination(in order, out var targetWorldCm))
            {
                return;
            }

            int prefabId = ResolveCueMarkerPrefabId();
            if (prefabId <= 0)
            {
                return;
            }

            _presentationCommands.TryAdd(new PresentationCommand
            {
                Kind = PresentationCommandKind.PlayOneShotPerformer,
                IdA = prefabId,
                Position = new System.Numerics.Vector3(
                    WorldUnits.CmToM(targetWorldCm.X),
                    0.15f,
                    WorldUnits.CmToM(targetWorldCm.Z)),
                Param0 = accepted
                    ? new System.Numerics.Vector4(0.28f, 0.94f, 0.60f, 1f)
                    : new System.Numerics.Vector4(1.0f, 0.52f, 0.18f, 1f),
                Param1 = accepted ? 0.75f : 0.90f
            });
        }

        private int ResolveCueMarkerPrefabId()
        {
            if (_cueMarkerPrefabId > 0)
            {
                return _cueMarkerPrefabId;
            }

            if (_prefabs == null)
            {
                return 0;
            }

            _cueMarkerPrefabId = _prefabs.GetId(WellKnownPrefabKeys.CueMarker);
            return _cueMarkerPrefabId;
        }
    }
}
