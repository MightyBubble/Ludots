// M2.b 部队标记:内核 troopsSet(部队 cell 格坐标)投影成引擎世界摆点,经
// sango.troop.marker presenter(assets/Presentation/presenters.json,cube 立方体,
// 区别于城池 sphere)渲染势力着色标记;坐标换算与 SangoCityMarkers 同轴(内核格
// x=北、y=东,引擎世界以地图中心为原点,格边 GridSize 米)。
// 与城池标记的差异:部队会生灭和移动,这里维护一个增量 runtime——订阅内核
// GameEvent.OnTroopCreated/OnTroopEnterCell/OnTroopClear/OnTroopDestroyed,事件到达即
// 增删改标记实体(owner 实体 VisualTransform 变更由引擎 PresenterEntityTransformSyncSystem
// 逐帧跟随);数据源始终是内核 PONO(troopsSet),不建引擎侧平行状态。

using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Platform.Abstractions;

namespace Sango.Runtime
{
    public sealed record SangoTroopMarkerPlacement(
        int TroopId,
        string Name,
        Vector3 PositionCm,
        Vector4 ForceColor,
        int ForceId);

    public static class SangoTroopMarkers
    {
        public const string PresenterDefinitionKey = "sango.troop.marker";
        public const string ColorParamKey = "sango.troop.marker.color";

        internal static readonly Vector4 UnownedColor = new(0.55f, 0.55f, 0.55f, 1f);

        /// <summary>
        /// 纯投影:troopsSet 存活部队 → (部队 id、名称、世界 cm 摆点、势力色)。测试可
        /// headless 断言;坐标换算见文件头(与 M2.a 城标一致)。
        /// </summary>
        public static List<SangoTroopMarkerPlacement> BuildPlacements(Sango.Core.Scenario scenario)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            Sango.Core.Map map = scenario.Map;
            if (map?.CellSet?.GetCell(0, 0) == null)
                throw new InvalidOperationException("SangoTroopMarkers requires a loaded kernel map (real bin or synthetic grid).");

            float cellMeters = map.GridSize;
            int halfWorldCm = (int)(map.Width * cellMeters * 100f / 2f);
            var placements = new List<SangoTroopMarkerPlacement>();

            scenario.troopsSet.ForEach(troop =>
            {
                if (troop == null || !troop.IsAlive || troop.cell == null)
                {
                    return;
                }

                var positionCm = new Vector3(
                    (troop.cell.y * cellMeters + cellMeters * 0.5f) * 100f - halfWorldCm,
                    0f,
                    (troop.cell.x * cellMeters + cellMeters * 0.5f) * 100f - halfWorldCm);

                Sango.Core.Force? force = troop.mBelongForce;
                Vector4 color = UnownedColor;
                if (force?.mFlag != null)
                {
                    var flagColor = force.mFlag.color;
                    color = new Vector4(flagColor.r, flagColor.g, flagColor.b, 1f);
                }

                placements.Add(new SangoTroopMarkerPlacement(troop.Id, troop.Name ?? string.Empty, positionCm, color, force?.Id ?? 0));
            });

            return placements;
        }
    }

    /// <summary>
    /// 增量标记运行时:内核部队事件 → presenter 实体增删改。只在引擎 presenter 管线
    /// 可用的宿主(SangoSimModEntry 的 MapLoaded 门)创建;事件在内核侧触发(回合推进/
    /// 命令结算),引擎世界写入与事件同线程。内核未启动时事件不会到达,无需判空。
    /// </summary>
    public sealed class SangoTroopMarkerRuntime : IDisposable
    {
        readonly World _world;
        readonly PresenterEntityRuntime _presenterRuntime;
        readonly PresenterDefinitionRegistry _definitions;
        readonly PresentationStableIdAllocator _stableIds;
        readonly int _definitionId;
        readonly int _colorParamKey;
        // scopeId 命名空间说明:城池标记已用原始 city id 作 scope,部队 id 与城 id 同为
        // 各自对象池的 1..N 序号,直接混用会串;这里不依赖 DestroyScope,逐 presenter 销毁,
        // scope 仍写部队 id 仅作调试索引。
        readonly Dictionary<int, (Entity Owner, Entity Presenter)> _markersByTroopId = new();
        bool _disposed;

        public SangoTroopMarkerRuntime(
            World world,
            PresenterEntityRuntime presenterRuntime,
            PresenterDefinitionRegistry definitions,
            PresentationStableIdAllocator stableIds)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _presenterRuntime = presenterRuntime ?? throw new ArgumentNullException(nameof(presenterRuntime));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _stableIds = stableIds ?? throw new ArgumentNullException(nameof(stableIds));

            _definitionId = definitions.GetId(SangoTroopMarkers.PresenterDefinitionKey);
            if (_definitionId <= 0 || !definitions.TryGet(_definitionId, out PresenterDefinition? definition) ||
                definition == null)
            {
                throw new InvalidOperationException(
                    $"Presenter definition '{SangoTroopMarkers.PresenterDefinitionKey}' is not registered (assets/Presentation/presenters.json).");
            }

            _colorParamKey = PresenterParamKeyRegistry.Register(SangoTroopMarkers.ColorParamKey);

            Sango.Core.GameEvent.OnTroopCreated -= OnTroopCreated;
            Sango.Core.GameEvent.OnTroopCreated += OnTroopCreated;
            Sango.Core.GameEvent.OnTroopEnterCell -= OnTroopEnterCell;
            Sango.Core.GameEvent.OnTroopEnterCell += OnTroopEnterCell;
            Sango.Core.GameEvent.OnTroopClear -= OnTroopGone;
            Sango.Core.GameEvent.OnTroopClear += OnTroopGone;
            Sango.Core.GameEvent.OnTroopDestroyed -= OnTroopDestroyed;
            Sango.Core.GameEvent.OnTroopDestroyed += OnTroopDestroyed;
        }

        public int ActiveMarkers => _markersByTroopId.Count;

        /// <summary>全量对账(挂点初次同步用):按 BuildPlacements 补建/裁剪/搬位。</summary>
        public void SyncAll(Sango.Core.Scenario scenario)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SangoTroopMarkerRuntime));

            var alive = new HashSet<int>();
            foreach (SangoTroopMarkerPlacement placement in SangoTroopMarkers.BuildPlacements(scenario))
            {
                alive.Add(placement.TroopId);
                if (_markersByTroopId.TryGetValue(placement.TroopId, out var marker) && _world.IsAlive(marker.Owner))
                {
                    MoveOwner(marker.Owner, placement.PositionCm);
                }
                else
                {
                    SpawnMarker(placement);
                }
            }

            var stale = new List<int>();
            foreach (int troopId in _markersByTroopId.Keys)
            {
                if (!alive.Contains(troopId))
                {
                    stale.Add(troopId);
                }
            }

            foreach (int troopId in stale)
            {
                RemoveMarker(troopId);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Sango.Core.GameEvent.OnTroopCreated -= OnTroopCreated;
            Sango.Core.GameEvent.OnTroopEnterCell -= OnTroopEnterCell;
            Sango.Core.GameEvent.OnTroopClear -= OnTroopGone;
            Sango.Core.GameEvent.OnTroopDestroyed -= OnTroopDestroyed;
            _disposed = true;
        }

        void OnTroopCreated(Sango.Core.Troop troop, Sango.Core.Scenario scenario) => SpawnMarker(ToPlacement(troop, scenario));

        // 移动逐步发生(每格一个事件),标记只在格变更时搬位;grounding 是 Once 策略,
        // 逐格重贴地高度差可忽略(真图格 20m 内高差由 SnapToGround 采样一次即可)。
        void OnTroopEnterCell(Sango.Core.Troop troop, Sango.Core.Cell destCell, Sango.Core.Cell lastCell)
        {
            if (troop == null || !troop.IsAlive)
            {
                return;
            }

            if (!_markersByTroopId.TryGetValue(troop.Id, out var marker) || !_world.IsAlive(marker.Owner))
            {
                return;
            }

            Sango.Core.Map? map = Sango.Core.Scenario.Cur?.Map;
            if (map == null)
            {
                return;
            }

            ref VisualTransform transform = ref _world.Get<VisualTransform>(marker.Owner);
            transform.Position = CellToPositionCm(map, destCell);
        }

        void OnTroopGone(Sango.Core.Troop troop, Sango.Core.Scenario scenario) => RemoveMarker(troop.Id);

        void OnTroopDestroyed(Sango.Core.Troop troop, Sango.Core.SangoObject attacker, int damage, Sango.Core.Scenario scenario) => RemoveMarker(troop.Id);

        SangoTroopMarkerPlacement ToPlacement(Sango.Core.Troop troop, Sango.Core.Scenario scenario)
        {
            Sango.Core.Map map = scenario.Map ?? throw new InvalidOperationException("Troop marker projection requires the kernel map.");
            Sango.Core.Force? force = troop.mBelongForce;
            Vector4 color = SangoTroopMarkers.UnownedColor;
            if (force?.mFlag != null)
            {
                var flagColor = force.mFlag.color;
                color = new Vector4(flagColor.r, flagColor.g, flagColor.b, 1f);
            }

            return new SangoTroopMarkerPlacement(
                troop.Id,
                troop.Name ?? string.Empty,
                CellToPositionCm(map, troop.cell),
                color,
                force?.Id ?? 0);
        }

        static Vector3 CellToPositionCm(Sango.Core.Map map, Sango.Core.Cell cell)
        {
            float cellMeters = map.GridSize;
            int halfWorldCm = (int)(map.Width * cellMeters * 100f / 2f);
            return new Vector3(
                (cell.y * cellMeters + cellMeters * 0.5f) * 100f - halfWorldCm,
                0f,
                (cell.x * cellMeters + cellMeters * 0.5f) * 100f - halfWorldCm);
        }

        // 与 SangoCityMarkers.Spawn 同一条正式批量路径(单元素批),scope=部队 id,
        // per-instance 势力色 param 覆盖。
        void SpawnMarker(SangoTroopMarkerPlacement placement)
        {
            if (_disposed || _markersByTroopId.ContainsKey(placement.TroopId))
            {
                return;
            }

            Entity owner = _world.Create(
                new VisualTransform
                {
                    Position = placement.PositionCm,
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                new CullState { IsVisible = true, LOD = LODLevel.High });

            var owners = new[] { owner };
            var scopeIds = new[] { placement.TroopId };
            var stableIds = new[] { _stableIds.Allocate() };
            var transforms = new[] { _world.Get<VisualTransform>(owner) };
            var culls = new[] { _world.Get<CullState>(owner) };
            var colorOverrides = new[]
            {
                new[]
                {
                    new ParamDefault
                    {
                        ParamKey = _colorParamKey,
                        Lane = ParamLane.Vector,
                        VectorValue = placement.ForceColor,
                    },
                },
            };

            var created = new Entity[1];
            _presenterRuntime.CreateEntityAnchoredRootBatch(
                _definitions,
                _definitionId,
                owners,
                scopeIds,
                stableIds,
                transforms,
                culls,
                definition: null,
                created,
                _stableIds.Allocate,
                colorOverrides);
            _markersByTroopId[placement.TroopId] = (owner, created[0]);
        }

        void MoveOwner(Entity owner, Vector3 positionCm)
        {
            ref VisualTransform transform = ref _world.Get<VisualTransform>(owner);
            transform.Position = positionCm;
        }

        void RemoveMarker(int troopId)
        {
            if (!_markersByTroopId.TryGetValue(troopId, out var marker))
            {
                return;
            }

            _markersByTroopId.Remove(troopId);
            if (_world.IsAlive(marker.Presenter))
            {
                _presenterRuntime.Destroy(marker.Presenter);
            }

            if (_world.IsAlive(marker.Owner))
            {
                _world.Destroy(marker.Owner);
            }
        }
    }
}
