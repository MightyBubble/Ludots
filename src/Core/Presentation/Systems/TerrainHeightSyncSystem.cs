using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Map.Hex;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// 在 WorldToVisualSyncSystem 之后运行，为 VisualTransform 的 Y 分量采样地形高度。
    /// 
    /// 逻辑层 WorldPositionCm 仅包含 XY 平面（厘米），无高度信息。
    /// 表现层需将实体贴附到地形表面，避免悬浮或穿地。
    /// 
    /// 仅处理带 WorldPositionCm 的实体，避免影响输入/相机等锚点实体。
    /// </summary>
    public sealed class TerrainHeightSyncSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly IReadOnlyDictionary<string, object> _globals;
        private static readonly QueryDescription _query = new QueryDescription()
            .WithAll<WorldPositionCm, VisualTransform>();

        /// <summary>地形高度缩放（米/高度单位），需与地形渲染器一致，默认 2.0。</summary>
        public float HeightScale { get; set; } = 2.0f;

        public TerrainHeightSyncSystem(World world, IReadOnlyDictionary<string, object> globals)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            if (!_globals.TryGetValue(ContextKeys.VertexMap, out var vtxObj) || vtxObj is not VertexMap vertexMap)
                return;

            var job = new SyncJob { VertexMap = vertexMap, HeightScale = HeightScale };
            _world.InlineQuery<SyncJob, WorldPositionCm, VisualTransform>(in _query, ref job);
        }

        public void AfterUpdate(in float dt) { }
        public void BeforeUpdate(in float dt) { }
        public void Dispose() { }

        private struct SyncJob : IForEach<WorldPositionCm, VisualTransform>
        {
            public VertexMap VertexMap;
            public float HeightScale;

            public void Update(ref WorldPositionCm _, ref VisualTransform visual)
            {
                var pos = visual.Position;
                float rawHeight;
                try
                {
                    rawHeight = VertexMap.GetLogicHeight(pos);
                }
                catch
                {
                    return;
                }
                if (float.IsNaN(rawHeight) || float.IsInfinity(rawHeight)) return;
                pos.Y = rawHeight * HeightScale;
                visual.Position = pos;
            }
        }
    }
}
