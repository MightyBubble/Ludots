using System;
using System.Numerics;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Spatial;

namespace Ludots.Core.Systems
{
    public class CameraCullingSystem : BaseSystem<World, float>
    {
        private readonly CameraManager _cameraManager;
        private readonly ISpatialQueryService _spatial;
        private readonly IViewController _view;
        private Entity[] _buffer = new Entity[4096];
        private HashSet<Entity> _prevVisible = new HashSet<Entity>();
        private HashSet<Entity> _nextVisible = new HashSet<Entity>();
        
        /// <summary>
        /// LOD 距离阈值（厘米）。实体到相机距离小于该值则使用对应 LOD。
        /// </summary>
        public float HighLODDistCm = 4000f;    // < 40m (High)
        public float MediumLODDistCm = 10000f;  // < 100m (Medium)
        public float LowLODDistCm = 20000f;    // < 200m (Low)
        // > LowLODDistCm → Culled

        public CameraCullingSystem(World world, CameraManager cameraManager, ISpatialQueryService spatial, IViewController view) : base(world) 
        {
            _cameraManager = cameraManager;
            _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
            _view = view ?? throw new ArgumentNullException(nameof(view));
        }

        public override void Update(in float dt)
        {
            var target = _cameraManager.State.TargetCm;
            float distanceCm = _cameraManager.State.DistanceCm;
            
            // Calculate Logic Viewport Size
            float fovY = _cameraManager.State.FovYDeg * (float)(Math.PI / 180.0f);
            float aspectRatio = _view.AspectRatio;
            float pitchRad = _cameraManager.State.Pitch * (float)(Math.PI / 180.0f);
            
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

            var r = _spatial.QueryAabb(new Ludots.Core.Mathematics.WorldAabbCm(ix, iy, iw, ih), _buffer);
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
                if (!World.Has<WorldPositionCm>(e) || !World.Has<CullState>(e) || !World.Has<VisualModel>(e)) continue;

                var wp = World.Get<WorldPositionCm>(e).Value;
                float px = wp.X.ToFloat();
                float py = wp.Y.ToFloat();
                bool inViewport = (px >= minX && px <= maxX && py >= minY && py <= maxY);

                ref var cull = ref World.Get<CullState>(e);
                if (!inViewport) { cull.LOD = LODLevel.Culled; cull.IsVisible = false; continue; }

                // 2. Distance Check (Logic Space)
                float dx = px - tx;
                float dy = py - ty;
                float distSq = dx*dx + dy*dy;
                
                cull.DistanceToCameraSq = distSq;

                // 3. LOD Selection
                if (distSq < highSq)
                {
                    cull.LOD = LODLevel.High;
                    cull.IsVisible = true;
                    _nextVisible.Add(e);
                }
                else if (distSq < medSq)
                {
                    cull.LOD = LODLevel.Medium;
                    cull.IsVisible = true;
                    _nextVisible.Add(e);
                }
                else if (distSq < lowSq2)
                {
                    cull.LOD = LODLevel.Low;
                    cull.IsVisible = true;
                    _nextVisible.Add(e);
                }
                else
                {
                    cull.LOD = LODLevel.Culled;
                    cull.IsVisible = false;
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
        }
    }
}
