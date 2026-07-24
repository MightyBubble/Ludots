using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Vision
{
    public sealed class VisionSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription EmitterQuery = new QueryDescription()
            .WithAll<VisionEmitterCm, WorldPositionCm>();

        private static readonly QueryDescription OccupantQuery = new QueryDescription()
            .WithAll<FogOccupantCm, WorldPositionCm>();

        private readonly GameSession _session;
        private readonly FogLayerRegistry _layers;
        private readonly FogFieldStore _fields;
        private readonly VisionResolver _resolver;
        private readonly FogKnowledgeProjector _projector;
        private readonly KnowledgeProjectionStore _knowledge;
        private readonly PlayerEntityLookup _players;
        private FogLayerId[] _layerIds = new FogLayerId[8];
        private FogField[] _fieldScratch = new FogField[8];
        private FogOccupant[] _occupants = new FogOccupant[32];
        private EmitterFrame[] _emitters = new EmitterFrame[16];

        public VisionSystem(
            World world,
            GameSession session,
            FogLayerRegistry layers,
            FogFieldStore fields,
            VisionResolver resolver,
            FogKnowledgeProjector projector,
            KnowledgeProjectionStore knowledge,
            PlayerEntityLookup players)
            : base(world)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _layers = layers ?? throw new ArgumentNullException(nameof(layers));
            _fields = fields ?? throw new ArgumentNullException(nameof(fields));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _projector = projector ?? throw new ArgumentNullException(nameof(projector));
            _knowledge = knowledge ?? throw new ArgumentNullException(nameof(knowledge));
            _players = players ?? throw new ArgumentNullException(nameof(players));
        }

        public override void Update(in float dt)
        {
            int layerCount = CopyLayerIds();
            if (layerCount <= 0)
            {
                _knowledge.RunMaintenance(_session.CurrentTick);
                return;
            }

            ReadOnlySpan<FogLayerId> targetLayers = _layerIds.AsSpan(0, layerCount);
            int occupantCellSizeCm = ResolveSharedCellSizeCm(targetLayers);
            AgeVisibleFields();
            int occupantCount = CopyOccupants(occupantCellSizeCm);
            int emitterCount = CopyEmitters();
            var rules = FogRulesPolicy.Default;
            var projection = FogProjectionPolicy.Default;

            for (int i = 0; i < emitterCount; i++)
            {
                ref EmitterFrame frame = ref _emitters[i];
                _resolver.Resolve(frame.Emitter, targetLayers, in rules);
            }

            if (occupantCount > 0)
            {
                ReadOnlySpan<FogOccupant> occupants = _occupants.AsSpan(0, occupantCount);
                int currentTick = _session.CurrentTick;
                for (int i = 0; i < emitterCount; i++)
                {
                    ref EmitterFrame frame = ref _emitters[i];
                    for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
                    {
                        FogLayerId layerId = targetLayers[layerIndex];
                        if ((frame.Emitter.LayerMask & _layers.ToMask(layerId)) == 0u ||
                            !_fields.TryGet(frame.Emitter.ScopeKeyId, layerId, out FogField field))
                        {
                            continue;
                        }

                        byte detectionStrength = Math.Max(frame.Emitter.DetectionStrength, frame.Emitter.TrueSightStrength);
                        _projector.Project(
                            frame.Viewer,
                            frame.Entity,
                            frame.Emitter.Position,
                            field,
                            occupants,
                            in projection,
                            currentTick,
                            detectionStrength);
                    }
                }
            }

            _knowledge.RunMaintenance(_session.CurrentTick);
        }

        private int CopyLayerIds()
        {
            EnsureLayerCapacity(_layers.Count);
            return _layers.CopyLayerIds(_layerIds);
        }

        private void AgeVisibleFields()
        {
            EnsureFieldCapacity(_fields.Count);
            int fieldCount = _fields.CopyFields(_fieldScratch);
            for (int i = 0; i < fieldCount; i++)
            {
                _fieldScratch[i].AgeVisibleToExplored();
            }
        }

        private int CopyEmitters()
        {
            int count = 0;
            foreach (ref var chunk in World.Query(in EmitterQuery))
            {
                ref Entity firstEntity = ref chunk.Entity(0);
                var emitters = chunk.GetSpan<VisionEmitterCm>();
                var positions = chunk.GetSpan<WorldPositionCm>();
                foreach (int index in chunk)
                {
                    ref VisionEmitterCm emitter = ref emitters[index];
                    if (emitter.ScopeKeyId <= 0 || emitter.LayerMask == 0u)
                    {
                        continue;
                    }

                    EnsureEmitterCapacity(count + 1);
                    Entity entity = Unsafe.Add(ref firstEntity, index);
                    Entity viewer = ResolveViewer(entity);
                    _emitters[count++] = new EmitterFrame(
                        entity,
                        viewer,
                        new VisionEmitter(
                            emitter.ScopeKeyId,
                            positions[index].ToWorldCmInt2(),
                            ResolveFacingDeg(entity),
                            emitter.LayerMask,
                            emitter.Polarity,
                            emitter.Aperture,
                            emitter.AltitudeBand,
                            emitter.Priority,
                            emitter.TargetScopeSelectorId,
                            emitter.DetectionStrength,
                            emitter.TrueSightStrength));
                }
            }

            return count;
        }

        private Entity ResolveViewer(Entity emitter)
        {
            if (!World.TryGet(emitter, out PlayerOwner owner))
            {
                return emitter;
            }

            if (owner.PlayerId <= 0 ||
                !_players.TryGet(owner.PlayerId, out Entity player) ||
                !World.IsAlive(player))
            {
                throw new InvalidOperationException(
                    $"Vision emitter declares PlayerOwner {owner.PlayerId} without a live formal player representative.");
            }

            return player;
        }

        private int CopyOccupants(int cellSizeCm)
        {
            int count = 0;
            foreach (ref var chunk in World.Query(in OccupantQuery))
            {
                ref Entity firstEntity = ref chunk.Entity(0);
                var occupants = chunk.GetSpan<FogOccupantCm>();
                var positions = chunk.GetSpan<WorldPositionCm>();
                foreach (int index in chunk)
                {
                    ref FogOccupantCm occupant = ref occupants[index];
                    if (occupant.ExposeLayerMask == 0u)
                    {
                        continue;
                    }

                    EnsureOccupantCapacity(count + 1);
                    Entity entity = Unsafe.Add(ref firstEntity, index);
                    WorldCmInt2 position = positions[index].ToWorldCmInt2();
                    _occupants[count++] = new FogOccupant(
                        entity,
                        position,
                        occupant.ExposeLayerMask,
                        occupant.AltitudeBand,
                        occupant.StealthLevel,
                        ResolvePrecomputedCell(position, cellSizeCm),
                        cellSizeCm);
                }
            }

            return count;
        }

        private int ResolveSharedCellSizeCm(ReadOnlySpan<FogLayerId> targetLayers)
        {
            int cellSizeCm = 0;
            for (int i = 0; i < targetLayers.Length; i++)
            {
                int current = _layers.Get(targetLayers[i]).CellSizeCm;
                if (cellSizeCm == 0)
                {
                    cellSizeCm = current;
                    continue;
                }

                if (cellSizeCm != current)
                {
                    return 0;
                }
            }

            return cellSizeCm;
        }

        private static FogCell ResolvePrecomputedCell(WorldCmInt2 position, int cellSizeCm)
        {
            return cellSizeCm > 0
                ? new FogCell(
                    MathUtil.FloorDiv(position.X, cellSizeCm),
                    MathUtil.FloorDiv(position.Y, cellSizeCm))
                : default;
        }

        private int ResolveFacingDeg(Entity entity)
        {
            if (!World.IsAlive(entity) || !World.Has<FacingDirection>(entity))
            {
                return 0;
            }

            ref FacingDirection facing = ref World.Get<FacingDirection>(entity);
            return (int)MathF.Round(WorldPlane2D.NormalizeDegreesPositive(WorldPlane2D.RadToDegValue(facing.AngleRad)));
        }

        private void EnsureLayerCapacity(int required)
        {
            if (required > _layerIds.Length)
            {
                Array.Resize(ref _layerIds, NextCapacity(_layerIds.Length, required));
            }
        }

        private void EnsureFieldCapacity(int required)
        {
            if (required > _fieldScratch.Length)
            {
                Array.Resize(ref _fieldScratch, NextCapacity(_fieldScratch.Length, required));
            }
        }

        private void EnsureEmitterCapacity(int required)
        {
            if (required > _emitters.Length)
            {
                Array.Resize(ref _emitters, NextCapacity(_emitters.Length, required));
            }
        }

        private void EnsureOccupantCapacity(int required)
        {
            if (required > _occupants.Length)
            {
                Array.Resize(ref _occupants, NextCapacity(_occupants.Length, required));
            }
        }

        private static int NextCapacity(int current, int required)
        {
            int next = Math.Max(4, current);
            while (next < required)
            {
                next *= 2;
            }

            return next;
        }

        private readonly struct EmitterFrame
        {
            public EmitterFrame(Entity entity, Entity viewer, in VisionEmitter emitter)
            {
                Entity = entity;
                Viewer = viewer;
                Emitter = emitter;
            }

            public readonly Entity Entity;
            public readonly Entity Viewer;
            public readonly VisionEmitter Emitter;
        }
    }
}
