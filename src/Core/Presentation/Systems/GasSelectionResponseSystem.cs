using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// 通用的 GAS SelectionRequest 响应系统。
    /// 
    /// 当 GAS 技能系统发出 SelectionRequest（例如"选择范围内的敌方目标"）时，
    /// 此系统在下次用户点击时执行空间查询并返回 SelectionResponse。
    /// 
    /// 支持的 SelectionRequestTags：
    /// - Single: 选择最近的单个实体
    /// - CircleEnemy: 选择范围内的敌方实体（通过 Team 组件过滤）
    /// - CircleAll: 选择范围内的所有实体
    /// 
    /// Mod 可通过 OnSelectionTriggered 回调添加视觉反馈（如范围指示器）。
    /// </summary>
    public sealed class GasSelectionResponseSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly ISpatialQueryService _spatial;
        private SelectionRequest _activeRequest;
        private bool _hasActiveRequest;

        /// <summary>默认拾取半径（厘米），用于 Single 模式。</summary>
        public int PickRadiusCm { get; set; } = 120;

        /// <summary>
        /// 当 Selection 被触发时的回调。Mod 可注册此回调来添加视觉反馈（如范围圆圈）。
        /// 参数：(SelectionRequest request, WorldCmInt2 clickPoint)
        /// </summary>
        public Action<SelectionRequest, WorldCmInt2>? OnSelectionTriggered { get; set; }

        public GasSelectionResponseSystem(World world, Dictionary<string, object> globals, ISpatialQueryService spatial)
        {
            _world = world;
            _globals = globals;
            _spatial = spatial;
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            if (!_globals.TryGetValue(ContextKeys.InputHandler, out var inputObj) || inputObj is not PlayerInputHandler input) return;
            if (!_globals.TryGetValue(ContextKeys.ScreenRayProvider, out var rayObj) || rayObj is not IScreenRayProvider rayProvider) return;
            if (!_globals.TryGetValue(ContextKeys.SelectionRequestQueue, out var reqObj) || reqObj is not SelectionRequestQueue requests) return;
            if (!_globals.TryGetValue(ContextKeys.SelectionResponseBuffer, out var respObj) || respObj is not SelectionResponseBuffer responses) return;

            if (!_hasActiveRequest && requests.TryDequeue(out var req))
            {
                _activeRequest = req;
                _hasActiveRequest = true;
            }

            if (!_hasActiveRequest) return;
            if (!input.PressedThisFrame("Select")) return;

            var mouse = input.ReadAction<System.Numerics.Vector2>("PointerPos");
            var ray = rayProvider.GetRay(mouse);
            if (!GroundRaycastUtil.TryGetGroundWorldCm(in ray, out var worldCm)) return;

            OnSelectionTriggered?.Invoke(_activeRequest, worldCm);

            unsafe
            {
                var response = BuildResponse(_activeRequest, worldCm);
                responses.TryAdd(response);
                _hasActiveRequest = false;
                _activeRequest = default;
            }
        }

        private Entity FindNearestEntity(in WorldCmInt2 worldCm, int radiusCm)
        {
            Span<Entity> buffer = stackalloc Entity[256];
            var result = _spatial.QueryRadius(worldCm, radiusCm, buffer);
            int count = result.Count;
            if (count <= 0) return default;

            Entity best = default;
            long bestD2 = long.MaxValue;

            for (int i = 0; i < count; i++)
            {
                var e = buffer[i];
                if (!_world.IsAlive(e)) continue;
                ref var pos = ref _world.TryGetRef<WorldPositionCm>(e, out bool hasPos);
                if (!hasPos) continue;

                var cmPos = pos.Value.ToWorldCmInt2();
                long dx = cmPos.X - worldCm.X;
                long dy = cmPos.Y - worldCm.Y;
                long d2 = dx * dx + dy * dy;
                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    best = e;
                }
            }

            return best;
        }

        private unsafe SelectionResponse BuildResponse(in SelectionRequest request, in WorldCmInt2 worldCm)
        {
            var resp = default(SelectionResponse);
            resp.RequestId = request.RequestId;
            resp.ResponseTagId = request.RequestTagId;

            int maxCount = request.PayloadB > 0 ? request.PayloadB : 64;
            if (maxCount > 64) maxCount = 64;

            if (request.RequestTagId == SelectionRequestTags.Single)
            {
                var e = FindNearestEntity(worldCm, PickRadiusCm);
                if (_world.IsAlive(e))
                {
                    resp.Count = 1;
                    resp.EntityIds[0] = e.Id;
                    resp.WorldIds[0] = e.WorldId;
                    resp.Versions[0] = e.Version;
                }
                return resp;
            }

            int radiusCm = request.PayloadA > 0 ? request.PayloadA : 250;
            Span<Entity> buffer = stackalloc Entity[Math.Min(maxCount * 4, 512)];
            var result = _spatial.QueryRadius(worldCm, radiusCm, buffer);

            int originTeam = 0;
            if (_world.IsAlive(request.Origin))
            {
                ref var t = ref _world.TryGetRef<Team>(request.Origin, out bool hasTeam);
                if (hasTeam) originTeam = t.Id;
            }

            int written = 0;
            for (int i = 0; i < result.Count && written < maxCount; i++)
            {
                var e = buffer[i];
                if (!_world.IsAlive(e)) continue;
                if (request.RequestTagId == SelectionRequestTags.CircleEnemy)
                {
                    ref var team = ref _world.TryGetRef<Team>(e, out bool hasTeam);
                    if (!hasTeam) continue;
                    if (!RelationshipFilterUtil.Passes(RelationshipFilter.Hostile, originTeam, team.Id)) continue;
                }

                resp.EntityIds[written] = e.Id;
                resp.WorldIds[written] = e.WorldId;
                resp.Versions[written] = e.Version;
                written++;
            }

            resp.Count = written;
            return resp;
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
