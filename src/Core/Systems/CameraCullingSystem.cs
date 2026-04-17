using System;
using System.Numerics;
using System.Collections.Generic;
using System.Diagnostics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Map.Hex;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;

namespace Ludots.Core.Systems
{
    public class CameraCullingSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _presentationStateQuery = new QueryDescription()
            .WithAll<PresentationFrameState>();

        private readonly CameraManager _cameraManager;
        private readonly ISpatialQueryService _spatial;
        private readonly IViewController _view;
        private readonly MeshAssetRegistry? _meshes;
        private readonly ILoadedChunks? _loadedChunks;
        private readonly PresentationTimingDiagnostics? _timingDiagnostics;
        private Entity[] _buffer = new Entity[4096];
        private HashSet<Entity> _prevVisible = new HashSet<Entity>();
        private HashSet<Entity> _nextVisible = new HashSet<Entity>();

        public CameraCullingDebugState DebugState { get; } = new CameraCullingDebugState();
        
        /// <summary>
        /// LOD 距离阈值（厘米）。实体到相机距离小于该值则使用对应 LOD。
        /// </summary>
        public float HighLODDistCm = 4000f;    // < 40m (High)
        public float MediumLODDistCm = 10000f;  // < 100m (Medium)
        public float LowLODDistCm = 20000f;    // < 200m (Low)
        // > LowLODDistCm → Culled

        public CameraCullingSystem(
            World world,
            CameraManager cameraManager,
            ISpatialQueryService spatial,
            IViewController view,
            PresentationTimingDiagnostics? timingDiagnostics) : this(
                world,
                cameraManager,
                spatial,
                view,
                meshes: null,
                loadedChunks: null,
                timingDiagnostics)
        {
        }

        public CameraCullingSystem(
            World world,
            CameraManager cameraManager,
            ISpatialQueryService spatial,
            IViewController view,
            MeshAssetRegistry? meshes = null,
            ILoadedChunks? loadedChunks = null,
            PresentationTimingDiagnostics? timingDiagnostics = null) : base(world) 
        {
            _cameraManager = cameraManager;
            _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _meshes = meshes;
            _loadedChunks = loadedChunks;
            _timingDiagnostics = timingDiagnostics;
        }

        public override void Update(in float dt)
        {
            long start = Stopwatch.GetTimestamp();
            CameraStateSnapshot cameraState = _cameraManager.GetInterpolatedState(ReadPresentationAlpha());
            var target = cameraState.TargetCm;
            float distanceCm = cameraState.DistanceCm;
            
            // Calculate Logic Viewport Size
            float fovY = cameraState.FovYDeg * (float)(Math.PI / 180.0f);
            float aspectRatio = _view.AspectRatio;
            float pitchRad = cameraState.Pitch * (float)(Math.PI / 180.0f);
            
            // H = 2 * Distance * tan(FOV/2)
            float logicHeight = 2.0f * distanceCm * (float)Math.Tan(fovY / 2.0f);
            
            // Pitch Compensation (1/sin(pitch))
            float pitchScale = 1.0f / (float)Math.Max(Math.Sin(pitchRad), 0.1f);
            logicHeight *= pitchScale;
            
            float logicWidth = logicHeight * aspectRatio;
            
            // Buffer
            float buffer = 1.5f;
            logicWidth *= buffer;
            logicHeight *= buffer;

            // Define Logic Bounds
            float minX = target.X - logicWidth / 2f;
            float maxX = target.X + logicWidth / 2f;
            float minY = target.Y - logicHeight / 2f;
            float maxY = target.Y + logicHeight / 2f;

            _nextVisible.Clear();

            int ix = (int)MathF.Floor(minX);
            int iy = (int)MathF.Floor(minY);
            int iw = (int)MathF.Ceiling(maxX - minX);
            int ih = (int)MathF.Ceiling(maxY - minY);
            if (iw < 0) iw = 0;
            if (ih < 0) ih = 0;

            WorldAabbCm queryBounds = new WorldAabbCm(ix, iy, iw, ih);
            var r = _spatial.QueryAabb(queryBounds, _buffer);
            if (r.Dropped > 0 && _buffer.Length < 262144)
            {
                int next = _buffer.Length * 2;
                if (next < _buffer.Length + r.Dropped) next = _buffer.Length + r.Dropped;
                _buffer = new Entity[next];
            }

            float tx = target.X;
            float ty = target.Y;
            float highSq = HighLODDistCm * HighLODDistCm;
            float medSq = MediumLODDistCm * MediumLODDistCm;
            float lowSq2 = LowLODDistCm * LowLODDistCm;

            for (int idx = 0; idx < r.Count; idx++)
            {
                var e = _buffer[idx];
                if (!World.IsAlive(e)) continue;
                if (!World.Has<WorldPositionCm>(e) || !World.Has<CullState>(e) || !World.Has<VisualRuntimeState>(e)) continue;

                var visual = World.Get<VisualRuntimeState>(e);
                if (!visual.ShouldEmit)
                {
                    ref var hiddenCull = ref World.Get<CullState>(e);
                    hiddenCull.LOD = LODLevel.Culled;
                    hiddenCull.IsVisible = false;
                    continue;
                }

                var wp = World.Get<WorldPositionCm>(e).Value;
                float px = wp.X.ToFloat();
                float py = wp.Y.ToFloat();

                ref var cull = ref World.Get<CullState>(e);
                if (!PassesLoadedChunkGate(px, py) ||
                    !TryComputeScreenCoverageAndViewportIntersection(e, px, py, target, distanceCm, queryBounds, out float coverage01, out bool inViewport))
                {
                    cull.LOD = LODLevel.Culled;
                    cull.IsVisible = false;
                    cull.ScreenCoverage01 = 0f;
                    continue;
                }

                if (!inViewport)
                {
                    cull.LOD = LODLevel.Culled;
                    cull.IsVisible = false;
                    cull.ScreenCoverage01 = 0f;
                    continue;
                }

                // 2. Distance Check (Logic Space)
                float dx = px - tx;
                float dy = py - ty;
                float distSq = dx*dx + dy*dy;
                
                cull.DistanceToCameraSq = distSq;
                cull.ScreenCoverage01 = coverage01;

                // 3. LOD Selection
                LODLevel resolvedLod = ResolveLod(distSq, coverage01, highSq, medSq, lowSq2, in visual);
                cull.LOD = resolvedLod;
                cull.IsVisible = resolvedLod != LODLevel.Culled;
                if (cull.IsVisible)
                {
                    _nextVisible.Add(e);
                }
            }

            foreach (var e in _prevVisible)
            {
                if (_nextVisible.Contains(e)) continue;
                if (!World.IsAlive(e) || !World.Has<CullState>(e)) continue;
                ref var cull = ref World.Get<CullState>(e);
                cull.LOD = LODLevel.Culled;
                cull.IsVisible = false;
            }

            var tmp = _prevVisible;
            _prevVisible = _nextVisible;
            _nextVisible = tmp;

            DebugState.MinX = minX;
            DebugState.MaxX = maxX;
            DebugState.MinY = minY;
            DebugState.MaxY = maxY;
            DebugState.HighLodDist = HighLODDistCm;
            DebugState.MediumLodDist = MediumLODDistCm;
            DebugState.LowLodDist = LowLODDistCm;
            DebugState.CameraTargetCm = new System.Numerics.Vector2(target.X, target.Y);
            DebugState.VisibleEntityCount = _prevVisible.Count;
            _timingDiagnostics?.ObserveCameraCulling((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency, _prevVisible.Count);
        }

        private bool PassesLoadedChunkGate(float worldXCm, float worldYCm)
        {
            if (_loadedChunks == null || _loadedChunks.ActiveChunkKeys.Count == 0)
            {
                return true;
            }

            int cellX = (int)MathF.Floor(worldXCm / HexCoordinates.EdgeLengthCm);
            int cellY = (int)MathF.Floor(worldYCm / HexCoordinates.EdgeLengthCm);
            int chunkX = cellX >> 6;
            int chunkY = cellY >> 6;
            long key = HexCoordinates.GetChunkKey(chunkX, chunkY);
            return _loadedChunks.IsLoaded(key);
        }

        private bool TryComputeScreenCoverageAndViewportIntersection(
            Entity entity,
            float px,
            float py,
            Vector2 target,
            float distanceCm,
            in WorldAabbCm queryBounds,
            out float screenCoverage01,
            out bool intersectsViewport)
        {
            PresentationLocalBounds localBounds = ResolveBounds(entity);
            Vector3 center = World.Has<VisualTransform>(entity)
                ? World.Get<VisualTransform>(entity).Position
                : new Vector3(px * 0.01f, 0f, py * 0.01f);
            Vector3 scale = World.Has<VisualTransform>(entity)
                ? World.Get<VisualTransform>(entity).Scale
                : Vector3.One;

            float halfWidthCm = MathF.Max(10f, localBounds.Extents.X * MathF.Abs(scale.X) * 100f);
            float halfDepthCm = MathF.Max(10f, localBounds.Extents.Z * MathF.Abs(scale.Z) * 100f);
            float minX = (center.X * 100f) + (localBounds.Center.X * scale.X * 100f) - halfWidthCm;
            float maxX = minX + (halfWidthCm * 2f);
            float minY = (center.Z * 100f) + (localBounds.Center.Z * scale.Z * 100f) - halfDepthCm;
            float maxY = minY + (halfDepthCm * 2f);

            intersectsViewport = maxX >= queryBounds.Left &&
                                 minX <= queryBounds.Right &&
                                 maxY >= queryBounds.Top &&
                                 minY <= queryBounds.Bottom;

            float approxRadiusCm = MathF.Max(halfWidthCm, halfDepthCm);
            float distanceToCameraCm = MathF.Max(1f, MathF.Sqrt(((px - target.X) * (px - target.X)) + ((py - target.Y) * (py - target.Y)) + (distanceCm * distanceCm * 0.04f)));
            screenCoverage01 = Math.Clamp((approxRadiusCm * 2f) / MathF.Max(distanceToCameraCm, 1f), 0f, 1f);
            return true;
        }

        private PresentationLocalBounds ResolveBounds(Entity entity)
        {
            if (World.Has<PresentationLocalBounds>(entity))
            {
                return World.Get<PresentationLocalBounds>(entity);
            }

            if (_meshes != null && World.Has<VisualRuntimeState>(entity))
            {
                VisualRuntimeState visual = World.Get<VisualRuntimeState>(entity);
                int meshAssetId = visual.MeshAssetId;
                if (visual.LodProfile.HasValue)
                {
                    meshAssetId = visual.LodProfile.Value.High.MeshAssetId;
                }

                if (_meshes.TryGetDescriptor(meshAssetId, out MeshAssetDescriptor descriptor))
                {
                    if (descriptor.Type == MeshAssetType.ProceduralMesh && descriptor.ProceduralMeshData != null)
                    {
                        return PresentationLocalBounds.Create(descriptor.ProceduralMeshData.LocalBounds.Center, descriptor.ProceduralMeshData.LocalBounds.Extents);
                    }
                }
            }

            return PresentationLocalBounds.Create(Vector3.Zero, new Vector3(0.5f, 0.5f, 0.5f));
        }

        private static LODLevel ResolveLod(float distSq, float coverage01, float highSq, float medSq, float lowSq2, in VisualRuntimeState visual)
        {
            if (visual.LodProfile.HasValue)
            {
                VisualLodProfile profile = visual.LodProfile.Value;
                if (distSq <= (profile.High.MaxDistanceCm * profile.High.MaxDistanceCm) && coverage01 >= profile.High.MinScreenCoverage01)
                {
                    return LODLevel.High;
                }

                if (distSq <= (profile.Medium.MaxDistanceCm * profile.Medium.MaxDistanceCm) && coverage01 >= profile.Medium.MinScreenCoverage01)
                {
                    return LODLevel.Medium;
                }

                if (distSq <= (profile.Low.MaxDistanceCm * profile.Low.MaxDistanceCm) && coverage01 >= profile.Low.MinScreenCoverage01)
                {
                    return LODLevel.Low;
                }

                return LODLevel.Culled;
            }

            if (distSq < highSq)
            {
                return LODLevel.High;
            }

            if (distSq < medSq)
            {
                return LODLevel.Medium;
            }

            if (distSq < lowSq2)
            {
                return LODLevel.Low;
            }

            return LODLevel.Culled;
        }

        private float ReadPresentationAlpha()
        {
            var job = new ReadAlphaJob();
            World.InlineQuery<ReadAlphaJob, PresentationFrameState>(in _presentationStateQuery, ref job);
            return job.Alpha;
        }

        private struct ReadAlphaJob : IForEach<PresentationFrameState>
        {
            public float Alpha;

            public ReadAlphaJob()
            {
                Alpha = 1f;
            }

            public void Update(ref PresentationFrameState state)
            {
                Alpha = state.Enabled ? state.InterpolationAlpha : 1f;
            }
        }
    }
}
