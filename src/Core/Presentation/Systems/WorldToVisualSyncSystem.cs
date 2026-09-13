using System;
using System.Runtime.CompilerServices;
using Arch.Buffer;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Components;
using System.Numerics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// 统一的逻辑到表现层同步系统，负责从 WorldPositionCm 插值到 VisualTransform。
    /// 
    /// 这是表现层唯一的位置插值系统，实现"唯一真相"原则：
    /// - 逻辑层 SSOT: WorldPositionCm + PreviousWorldPositionCm (Fix64Vec2 定点数厘米)
    /// - 表现层输出: VisualTransform (浮点米)
    /// 
    /// 数据流（定点数统一架构）：
    ///   FixedUpdate:
    ///     SavePreviousWorldPositionSystem: Previous = Current
    ///     Physics/Nav/Network: 更新 Current (定点数)
    ///     Physics2DToWorldPositionSyncSystem: Position2D → WorldPositionCm (定点数直接赋值)
    ///   
    ///   RenderFrame:
    ///     PresentationFrameSetupSystem: 计算 InterpolationAlpha
    ///     WorldToVisualSyncSystem: Fix64Vec2.Lerp(Previous, Current, alpha) → VisualTransform
    /// 
    /// 坐标转换：
    ///   WorldPositionCm (Fix64Vec2 厘米, XY平面) → VisualTransform (浮点米, XZ平面)
    /// </summary>
    public sealed class WorldToVisualSyncSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _stateQuery = new QueryDescription()
            .WithAll<PresentationFrameState>();

        private static readonly QueryDescription _staticPendingFacingQuery = new QueryDescription()
            .WithAll<WorldPositionCm, PreviousWorldPositionCm, VisualTransform, PresentationStaticTransform, PresentationStaticVisualPending, FacingDirection>();

        private static readonly QueryDescription _staticPendingNoFacingQuery = new QueryDescription()
            .WithAll<WorldPositionCm, PreviousWorldPositionCm, VisualTransform, PresentationStaticTransform, PresentationStaticVisualPending>()
            .WithNone<FacingDirection>();
        
        private static readonly QueryDescription _noCullQuery = new QueryDescription()
            .WithAll<WorldPositionCm, PreviousWorldPositionCm, VisualTransform>()
            .WithNone<CullState, FacingDirection, PresentationStaticTransform>();
            
        private static readonly QueryDescription _withCullQuery = new QueryDescription()
            .WithAll<WorldPositionCm, PreviousWorldPositionCm, VisualTransform, CullState>()
            .WithNone<FacingDirection, PresentationStaticTransform>();
            
        // 带 FacingDirection 的查询（同步位置 + 旋转）
        private static readonly QueryDescription _facingNoCullQuery = new QueryDescription()
            .WithAll<WorldPositionCm, PreviousWorldPositionCm, VisualTransform, FacingDirection>()
            .WithNone<CullState, PresentationStaticTransform>();
            
        private static readonly QueryDescription _facingWithCullQuery = new QueryDescription()
            .WithAll<WorldPositionCm, PreviousWorldPositionCm, VisualTransform, FacingDirection, CullState>()
            .WithNone<PresentationStaticTransform>();
        private readonly CommandBuffer _commandBuffer = new();

        public WorldToVisualSyncSystem(World world) : base(world)
        {
        }

        public override void Update(in float dt)
        {
            // 1. 获取全局插值因子并转换为定点数（零分配 IForEach job）
            var readAlphaJob = new ReadAlphaJob();
            World.InlineQuery<ReadAlphaJob, PresentationFrameState>(in _stateQuery, ref readAlphaJob);
            Fix64 alpha = readAlphaJob.Alpha;

            var staticNoFacingJob = new SyncStaticNoFacingJob
            {
                Alpha = alpha,
                CommandBuffer = _commandBuffer,
            };
            World.InlineEntityQuery<SyncStaticNoFacingJob, WorldPositionCm, PreviousWorldPositionCm, VisualTransform>(
                in _staticPendingNoFacingQuery,
                ref staticNoFacingJob);

            var staticFacingJob = new SyncStaticFacingJob
            {
                Alpha = alpha,
                CommandBuffer = _commandBuffer,
            };
            World.InlineEntityQuery<SyncStaticFacingJob, WorldPositionCm, PreviousWorldPositionCm, VisualTransform, FacingDirection>(
                in _staticPendingFacingQuery,
                ref staticFacingJob);
            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }

            // 2. chunk 级三元组 Span 批插值：alpha 已提出循环，逐实体零组件解析
            SyncPositions(in _noCullQuery, alpha);
            SyncPositions(in _withCullQuery, alpha);
            SyncPositionsWithFacing(in _facingNoCullQuery, alpha);
            SyncPositionsWithFacing(in _facingWithCullQuery, alpha);
        }

        private void SyncPositions(in QueryDescription query, Fix64 alpha)
        {
            foreach (ref Chunk chunk in World.Query(in query))
            {
                ReadOnlySpan<WorldPositionCm> currents = chunk.GetSpan<WorldPositionCm>();
                ReadOnlySpan<PreviousWorldPositionCm> previouses = chunk.GetSpan<PreviousWorldPositionCm>();
                Span<VisualTransform> visuals = chunk.GetSpan<VisualTransform>();
                int count = chunk.Count;
                for (int i = 0; i < count; i++)
                {
                    visuals[i].Position = InterpolateToVisual(in previouses[i].Value, in currents[i].Value, alpha);
                }
            }
        }

        private void SyncPositionsWithFacing(in QueryDescription query, Fix64 alpha)
        {
            foreach (ref Chunk chunk in World.Query(in query))
            {
                ReadOnlySpan<WorldPositionCm> currents = chunk.GetSpan<WorldPositionCm>();
                ReadOnlySpan<PreviousWorldPositionCm> previouses = chunk.GetSpan<PreviousWorldPositionCm>();
                Span<VisualTransform> visuals = chunk.GetSpan<VisualTransform>();
                ReadOnlySpan<FacingDirection> facings = chunk.GetSpan<FacingDirection>();
                int count = chunk.Count;
                for (int i = 0; i < count; i++)
                {
                    visuals[i].Position = InterpolateToVisual(in previouses[i].Value, in currents[i].Value, alpha);
                    visuals[i].Rotation = WorldPlane2D.FacingRadToVisualYRotation(facings[i].AngleRad);
                }
            }
        }

        public override void Dispose()
        {
            _commandBuffer.Dispose();
            base.Dispose();
        }

        private struct ReadAlphaJob : IForEach<PresentationFrameState>
        {
            public Fix64 Alpha;

            public ReadAlphaJob()
            {
                Alpha = Fix64.OneValue;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(ref PresentationFrameState state)
            {
                Alpha = state.Enabled ? Fix64.FromFloat(state.InterpolationAlpha) : Fix64.OneValue;
            }
        }

        private struct SyncStaticNoFacingJob : IForEachWithEntity<WorldPositionCm, PreviousWorldPositionCm, VisualTransform>
        {
            public Fix64 Alpha;
            public CommandBuffer CommandBuffer;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(Entity entity, ref WorldPositionCm current, ref PreviousWorldPositionCm previous, ref VisualTransform visual)
            {
                visual.Position = InterpolateToVisual(in previous.Value, in current.Value, Alpha);
                CommandBuffer.Remove<PresentationStaticVisualPending>(in entity);
            }
        }

        private struct SyncStaticFacingJob : IForEachWithEntity<WorldPositionCm, PreviousWorldPositionCm, VisualTransform, FacingDirection>
        {
            public Fix64 Alpha;
            public CommandBuffer CommandBuffer;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(Entity entity, ref WorldPositionCm current, ref PreviousWorldPositionCm previous, ref VisualTransform visual, ref FacingDirection facing)
            {
                visual.Position = InterpolateToVisual(in previous.Value, in current.Value, Alpha);
                visual.Rotation = WorldPlane2D.FacingRadToVisualYRotation(facing.AngleRad);
                CommandBuffer.Remove<PresentationStaticVisualPending>(in entity);
            }
        }

        /// <summary>
        /// 从 Fix64Vec2 (定点数厘米, XY) 插值并转换到 Visual 空间 (浮点米, XZ)。
        /// 插值在定点数域进行，仅在最终输出时转换为浮点。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 InterpolateToVisual(in Fix64Vec2 previous, in Fix64Vec2 current, Fix64 alpha)
        {
            Fix64Vec2 interpolated = Fix64Vec2.Lerp(previous, current, alpha);
            return WorldPlane2D.LogicCmToVisualMeters(in interpolated);
        }
    }
}
