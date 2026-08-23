using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Vision
{
    public readonly struct KnowledgeAreaRevealDescriptor
    {
        public const int MaxLayers = 4;

        public KnowledgeAreaRevealDescriptor(
            int scopeKeyId,
            int radiusCm,
            ReadOnlySpan<FogLayerId> layerIds,
            int memoryTtlTicks = 0,
            byte detectionStrength = 0)
        {
            if (scopeKeyId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scopeKeyId), "Knowledge area reveal requires a registered scope key.");
            }

            if (radiusCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(radiusCm), "Knowledge area reveal radius must be positive.");
            }

            if (layerIds.IsEmpty || layerIds.Length > MaxLayers)
            {
                throw new ArgumentOutOfRangeException(nameof(layerIds), $"Knowledge area reveal requires 1..{MaxLayers} fog layers.");
            }

            ScopeKeyId = scopeKeyId;
            RadiusCm = radiusCm;
            MemoryTtlTicks = memoryTtlTicks;
            DetectionStrength = detectionStrength;
            LayerCount = layerIds.Length;

            Layer0 = layerIds[0];
            Layer1 = layerIds.Length > 1 ? layerIds[1] : default;
            Layer2 = layerIds.Length > 2 ? layerIds[2] : default;
            Layer3 = layerIds.Length > 3 ? layerIds[3] : default;
        }

        public readonly int ScopeKeyId;
        public readonly int RadiusCm;
        public readonly int MemoryTtlTicks;
        public readonly byte DetectionStrength;
        public readonly int LayerCount;
        public readonly FogLayerId Layer0;
        public readonly FogLayerId Layer1;
        public readonly FogLayerId Layer2;
        public readonly FogLayerId Layer3;

        public void CopyLayers(Span<FogLayerId> destination)
        {
            if (destination.Length < LayerCount)
            {
                throw new ArgumentException("Destination span is smaller than the reveal layer count.", nameof(destination));
            }

            if (LayerCount > 0) destination[0] = Layer0;
            if (LayerCount > 1) destination[1] = Layer1;
            if (LayerCount > 2) destination[2] = Layer2;
            if (LayerCount > 3) destination[3] = Layer3;
        }
    }

    public readonly struct KnowledgeAreaRevealResult
    {
        public KnowledgeAreaRevealResult(int rasterizedCells, int projectedTargets, int decayedTargets)
        {
            RasterizedCells = rasterizedCells;
            ProjectedTargets = projectedTargets;
            DecayedTargets = decayedTargets;
        }

        public readonly int RasterizedCells;
        public readonly int ProjectedTargets;
        public readonly int DecayedTargets;
    }

    public sealed class KnowledgeAreaRevealRuntime
    {
        private static readonly QueryDescription OccupantQuery = new QueryDescription()
            .WithAll<FogOccupantCm, WorldPositionCm>();

        private readonly World _world;
        private readonly FogLayerRegistry _layers;
        private readonly FogFieldStore _fields;
        private readonly VisionResolver _resolver;
        private readonly FogKnowledgeProjector _projector;
        private FogOccupant[] _occupants = new FogOccupant[32];

        public KnowledgeAreaRevealRuntime(
            World world,
            FogLayerRegistry layers,
            FogFieldStore fields,
            VisionResolver resolver,
            FogKnowledgeProjector projector)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _layers = layers ?? throw new ArgumentNullException(nameof(layers));
            _fields = fields ?? throw new ArgumentNullException(nameof(fields));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _projector = projector ?? throw new ArgumentNullException(nameof(projector));
        }

        public KnowledgeAreaRevealResult Reveal(
            Entity viewer,
            Entity source,
            WorldCmInt2 center,
            in KnowledgeAreaRevealDescriptor descriptor,
            int currentTick)
        {
            if (!_world.IsAlive(viewer))
            {
                return default;
            }

            Entity recordSource = _world.IsAlive(source) ? source : viewer;
            Span<FogLayerId> layers = stackalloc FogLayerId[KnowledgeAreaRevealDescriptor.MaxLayers];
            descriptor.CopyLayers(layers);
            ReadOnlySpan<FogLayerId> activeLayers = layers[..descriptor.LayerCount];
            uint layerMask = BuildLayerMask(activeLayers);

            var emitter = new VisionEmitter(
                descriptor.ScopeKeyId,
                center,
                facingDeg: 0,
                layerMask,
                VisionPolarity.Reveal,
                VisionAperture.Disk(descriptor.RadiusCm),
                detectionStrength: descriptor.DetectionStrength);

            int rasterized = _resolver.Resolve(emitter, activeLayers, FogRulesPolicy.Default);
            int occupantCount = CopyOccupants();
            if (occupantCount == 0)
            {
                return new KnowledgeAreaRevealResult(rasterized, 0, 0);
            }

            int projected = 0;
            ReadOnlySpan<FogOccupant> occupants = _occupants.AsSpan(0, occupantCount);
            var projection = new FogProjectionPolicy(FogDisclosurePolicy.None, descriptor.MemoryTtlTicks);
            for (int i = 0; i < activeLayers.Length; i++)
            {
                FogLayerId layerId = activeLayers[i];
                if (!_fields.TryGet(descriptor.ScopeKeyId, layerId, out FogField field))
                {
                    continue;
                }

                projected += _projector.Project(
                    viewer,
                    recordSource,
                    center,
                    field,
                    occupants,
                    in projection,
                    currentTick,
                    descriptor.DetectionStrength);
            }

            return new KnowledgeAreaRevealResult(rasterized, projected, 0);
        }

        public KnowledgeAreaRevealResult DecayArea(
            Entity viewer,
            Entity source,
            WorldCmInt2 center,
            in KnowledgeAreaRevealDescriptor descriptor,
            int currentTick)
        {
            if (!_world.IsAlive(viewer))
            {
                return default;
            }

            Entity recordSource = _world.IsAlive(source) ? source : viewer;
            Span<FogLayerId> layers = stackalloc FogLayerId[KnowledgeAreaRevealDescriptor.MaxLayers];
            descriptor.CopyLayers(layers);
            ReadOnlySpan<FogLayerId> activeLayers = layers[..descriptor.LayerCount];
            uint layerMask = BuildLayerMask(activeLayers);

            int occupantCount = CopyOccupants();
            if (occupantCount == 0)
            {
                return default;
            }

            var projection = new FogProjectionPolicy(FogDisclosurePolicy.None, descriptor.MemoryTtlTicks);
            long radiusSq = (long)descriptor.RadiusCm * descriptor.RadiusCm;
            int decayed = 0;

            for (int i = 0; i < occupantCount; i++)
            {
                FogOccupant occupant = _occupants[i];
                if ((occupant.ExposeLayerMask & layerMask) == 0u || !IsWithinRadius(center, occupant.Position, radiusSq))
                {
                    continue;
                }

                _projector.Decay(viewer, occupant.Entity, recordSource, in projection, currentTick);
                decayed++;
            }

            return new KnowledgeAreaRevealResult(0, 0, decayed);
        }

        private uint BuildLayerMask(ReadOnlySpan<FogLayerId> layerIds)
        {
            uint mask = 0u;
            for (int i = 0; i < layerIds.Length; i++)
            {
                mask |= _layers.ToMask(layerIds[i]);
            }

            return mask;
        }

        private int CopyOccupants()
        {
            int count = 0;
            foreach (ref var chunk in _world.Query(in OccupantQuery))
            {
                ref Entity firstEntity = ref chunk.Entity(0);
                var occupants = chunk.GetSpan<FogOccupantCm>();
                var positions = chunk.GetSpan<WorldPositionCm>();
                foreach (int index in chunk)
                {
                    ref readonly FogOccupantCm occupant = ref occupants[index];
                    if (occupant.ExposeLayerMask == 0u)
                    {
                        continue;
                    }

                    EnsureOccupantCapacity(count + 1);
                    Entity entity = System.Runtime.CompilerServices.Unsafe.Add(ref firstEntity, index);
                    _occupants[count++] = new FogOccupant(
                        entity,
                        positions[index].ToWorldCmInt2(),
                        occupant.ExposeLayerMask,
                        occupant.AltitudeBand,
                        occupant.StealthLevel);
                }
            }

            return count;
        }

        private void EnsureOccupantCapacity(int required)
        {
            if (required <= _occupants.Length)
            {
                return;
            }

            int next = _occupants.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _occupants, next);
        }

        private static bool IsWithinRadius(WorldCmInt2 center, WorldCmInt2 point, long radiusSq)
        {
            int dx = point.X - center.X;
            int dy = point.Y - center.Y;
            return ((long)dx * dx) + ((long)dy * dy) <= radiusSq;
        }
    }
}
