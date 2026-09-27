using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using CoreInputMod.Systems;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Orders;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using RoadNetworkShowcaseMod.Gameplay;

namespace RoadNetworkShowcaseMod.Systems
{
    /// <summary>
    /// Road-network order policy (migration slice 2): the local order mapping installs through
    /// the CoreInputMod auto assembly; this policy system waits for that mapping and reroutes its
    /// submit handlers through the road move-order expander, emitting transient cue markers per
    /// submitted order.
    /// </summary>
    internal sealed class RoadNetworkOrderPolicySystem : ISystem<float>
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly RoadMoveOrderExpander _expander;
        private TransientMarkerBuffer? _transientMarkers;
        private MeshAssetRegistry? _meshes;
        private int _cueMarkerMeshId;
        private bool _attached;

        public RoadNetworkOrderPolicySystem(World world, Dictionary<string, object> globals, OrderQueue orders)
        {
            _world = world;
            _globals = globals;
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
            if (_attached ||
                !_globals.TryGetValue(CoreServiceKeys.ActiveInputOrderMapping.Name, out var mappingObj) ||
                mappingObj is not InputOrderMappingSystem mapping)
            {
                return;
            }

            _attached = true;
            _transientMarkers = _globals.TryGetValue(CoreServiceKeys.TransientMarkerBuffer.Name, out var markerObj) &&
                                markerObj is TransientMarkerBuffer transientMarkers
                ? transientMarkers
                : null;
            _meshes = _globals.TryGetValue(CoreServiceKeys.PresentationMeshAssetRegistry.Name, out var meshObj) &&
                      meshObj is MeshAssetRegistry meshes
                ? meshes
                : null;
            mapping.SetOrderSubmitHandler((in Order order) =>
            {
                _globals[LocalOrderSourceHelper.LastOrderDebugKey] =
                    $"type:{order.OrderTypeId},player:{order.PlayerId},actor:{order.Actor.Id}:{order.Actor.WorldId}:{order.Actor.Version},submit:{order.SubmitMode}";
                OrderSubmitResult result = _expander.TrySubmit(in order);
                EmitSubmitCue(in order, OrderSubmitResultSemantics.IsAccepted(result));
                return result;
            });
            mapping.SetOrderBatchSubmitHandler((Span<Order> orders) =>
            {
                if (orders.IsEmpty)
                {
                    return OrderSubmitResult.Queued;
                }

                _globals[LocalOrderSourceHelper.LastOrderDebugKey] =
                    $"type:{orders[0].OrderTypeId},player:{orders[0].PlayerId},actor:{orders[0].Actor.Id}:{orders[0].Actor.WorldId}:{orders[0].Actor.Version},submit:{orders[0].SubmitMode},batch:{orders.Length}";
                OrderSubmitResult result = _expander.TrySubmitSharedBatch(orders);
                bool accepted = OrderSubmitResultSemantics.IsAccepted(result);
                for (int i = 0; i < orders.Length; i++)
                {
                    EmitSubmitCue(in orders[i], accepted);
                }

                return result;
            });
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private void EmitSubmitCue(in Order order, bool accepted)
        {
            if (!OrderWorldSpatialResolver.TryResolveMoveDestination(_world, in order, out var targetWorldCm))
            {
                return;
            }

            TransientMarkerBuffer markers = _transientMarkers
                ?? throw new InvalidOperationException("Road network order cues require TransientMarkerBuffer.");
            int meshId = ResolveCueMarkerMeshId();
            if (!markers.TryAddMesh(
                meshId,
                new System.Numerics.Vector3(
                    WorldUnits.CmToM(targetWorldCm.X),
                    0.15f,
                    WorldUnits.CmToM(targetWorldCm.Z)),
                new System.Numerics.Vector3(accepted ? 0.75f : 0.90f),
                accepted
                    ? new System.Numerics.Vector4(0.28f, 0.94f, 0.60f, 1f)
                    : new System.Numerics.Vector4(1.0f, 0.52f, 0.18f, 1f),
                accepted ? 0.75f : 0.90f))
            {
                throw new InvalidOperationException("TransientMarkerBuffer is full while emitting road-network order cue.");
            }
        }

        private int ResolveCueMarkerMeshId()
        {
            if (_cueMarkerMeshId > 0)
            {
                return _cueMarkerMeshId;
            }

            if (_meshes == null)
            {
                throw new InvalidOperationException("Road network order cues require PresentationMeshAssetRegistry.");
            }

            _cueMarkerMeshId = WellKnownMeshKeys.RequireCueMarkerId(_meshes);
            return _cueMarkerMeshId;
        }
    }
}
