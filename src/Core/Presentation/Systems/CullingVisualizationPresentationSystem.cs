using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.System;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Reads <see cref="RenderCameraDebugState.DrawLogicalCullingDebug"/> and writes
    /// culling AABB + LOD rings to <see cref="DebugDrawCommandBuffer"/>.
    /// Purely Core layer — no platform dependency.
    /// </summary>
    public sealed class CullingVisualizationPresentationSystem : ISystem<float>
    {
        private readonly Dictionary<string, object> _globals;

        public CullingVisualizationPresentationSystem(Dictionary<string, object> globals)
        {
            _globals = globals;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float t) { }
        public void AfterUpdate(in float t) { }
        public void Dispose() { }

        public void Update(in float t)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.RenderCameraDebugState.Name, out var camObj) ||
                camObj is not RenderCameraDebugState camDebug ||
                !camDebug.DrawLogicalCullingDebug)
                return;

            if (!_globals.TryGetValue(CoreServiceKeys.CameraCullingDebugState.Name, out var cullObj) ||
                cullObj is not CameraCullingDebugState cull)
                return;

            if (!_globals.TryGetValue(CoreServiceKeys.DebugDrawCommandBuffer.Name, out var ddObj) ||
                ddObj is not DebugDrawCommandBuffer dd)
                return;

            var aabbCenter = new Vector2(
                (cull.MinX + cull.MaxX) * 0.5f,
                (cull.MinY + cull.MaxY) * 0.5f);
            float halfW = (cull.MaxX - cull.MinX) * 0.5f;
            float halfH = (cull.MaxY - cull.MinY) * 0.5f;

            dd.Boxes.Add(new DebugDrawBox2D
            {
                Center = aabbCenter,
                HalfWidth = halfW,
                HalfHeight = halfH,
                RotationRadians = 0f,
                Thickness = 2f,
                Color = DebugDrawColor.Cyan,
            });

            var camTarget = cull.CameraTargetCm;

            dd.Circles.Add(new DebugDrawCircle2D
            {
                Center = camTarget,
                Radius = cull.HighLodDist,
                Thickness = 1.5f,
                Color = DebugDrawColor.Green,
            });

            dd.Circles.Add(new DebugDrawCircle2D
            {
                Center = camTarget,
                Radius = cull.MediumLodDist,
                Thickness = 1.5f,
                Color = DebugDrawColor.Yellow,
            });

            dd.Circles.Add(new DebugDrawCircle2D
            {
                Center = camTarget,
                Radius = cull.LowLodDist,
                Thickness = 1.5f,
                Color = new DebugDrawColor(255, 128, 0),
            });
        }
    }
}
