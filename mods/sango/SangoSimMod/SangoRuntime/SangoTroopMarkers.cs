// M2.b 部队标记:内核部队经 sango.troop.marker presenter(assets/Presentation/
// presenters.json,cube 立方体,区别于城池 sphere)渲染势力着色标记;坐标换算与
// SangoCityMarkers 同轴(内核格 x=北、y=东,引擎世界以地图中心为原点,格边 GridSize 米)。
// D-3' presenter owner 迁移:标记 owner 从"标记专用 owner 实体"迁到原生部队实体
// (SangoTroopNativeRuntime 物化,自带 VisualTransform/CullState 锚定组件)——部队
// 实体随生灭事件物化/销毁,标记只做 presenter 挂接/摘除;移动不再手工搬位(引擎
// PresenterEntityTransformSyncSystem 逐帧跟随 owner VisualTransform,原生运行时在
// OnTroopEnterCell 逐格更新)。消灭标记/部队双实体(城域标记仍是独立 owner,M3.h
// 在案后续片)。

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
    /// 增量标记运行时(D-3' owner 迁移版):presenter 挂接到原生部队实体(owner),
    /// 生灭事件驱动挂接/摘除;摆位/移动由部队实体的 VisualTransform 承载(原生运行时
    /// 维护,引擎逐帧跟随)。只在引擎 presenter 管线可用且原生部队运行时挂载的宿主
    /// (SangoSimModEntry 的 MapLoaded 门,EnsureEntityMirror 先行)创建;原生运行时
    /// 缺席即类型化拒绝(部队实体不存在,不回落双实体路径)。
    /// </summary>
    public sealed class SangoTroopMarkerRuntime : IDisposable
    {
        readonly World _world;
        readonly SangoTroopNativeRuntime _troops;
        readonly PresenterEntityRuntime _presenterRuntime;
        readonly PresenterDefinitionRegistry _definitions;
        readonly PresentationStableIdAllocator _stableIds;
        readonly int _definitionId;
        readonly int _colorParamKey;
        // troopId → presenter 实体(owner 是部队实体,不在此表;摘除时只销 presenter)。
        readonly Dictionary<int, Entity> _presentersByTroopId = new();
        bool _disposed;

        public SangoTroopMarkerRuntime(
            World world,
            SangoTroopNativeRuntime troopRuntime,
            PresenterEntityRuntime presenterRuntime,
            PresenterDefinitionRegistry definitions,
            PresentationStableIdAllocator stableIds)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _troops = troopRuntime ?? throw new ArgumentNullException(
                nameof(troopRuntime),
                "SangoTroopMarkerRuntime requires the native troop runtime (presenter owner migration, D-3'); attach SangoTroopNativeRuntime first.");
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
            Sango.Core.GameEvent.OnTroopClear -= OnTroopGone;
            Sango.Core.GameEvent.OnTroopClear += OnTroopGone;
            Sango.Core.GameEvent.OnTroopDestroyed -= OnTroopDestroyed;
            Sango.Core.GameEvent.OnTroopDestroyed += OnTroopDestroyed;
        }

        public int ActiveMarkers => _presentersByTroopId.Count;

        /// <summary>全量对账(挂点初次同步/世界替换重建后):按 BuildPlacements 补挂/裁剪。</summary>
        public void SyncAll(Sango.Core.Scenario scenario)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SangoTroopMarkerRuntime));

            var alive = new HashSet<int>();
            foreach (SangoTroopMarkerPlacement placement in SangoTroopMarkers.BuildPlacements(scenario))
            {
                alive.Add(placement.TroopId);
                if (!_presentersByTroopId.ContainsKey(placement.TroopId))
                {
                    AttachMarker(placement);
                }
            }

            var stale = new List<int>();
            foreach (int troopId in _presentersByTroopId.Keys)
            {
                if (!alive.Contains(troopId))
                {
                    stale.Add(troopId);
                }
            }

            foreach (int troopId in stale)
            {
                DetachMarker(troopId);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Sango.Core.GameEvent.OnTroopCreated -= OnTroopCreated;
            Sango.Core.GameEvent.OnTroopClear -= OnTroopGone;
            Sango.Core.GameEvent.OnTroopDestroyed -= OnTroopDestroyed;
            _disposed = true;
        }

        void OnTroopCreated(Sango.Core.Troop troop, Sango.Core.Scenario scenario)
        {
            if (_disposed || troop == null || !troop.IsAlive || !CurrentKernel())
            {
                return;
            }

            AttachMarker(ToPlacement(troop, scenario));
        }

        void OnTroopGone(Sango.Core.Troop troop, Sango.Core.Scenario scenario)
        {
            if (troop == null || !CurrentKernel())
            {
                return;
            }

            DetachMarker(troop.Id);
        }

        void OnTroopDestroyed(Sango.Core.Troop troop, Sango.Core.SangoObject attacker, int damage, Sango.Core.Scenario scenario)
        {
            if (troop == null || !CurrentKernel())
            {
                return;
            }

            DetachMarker(troop.Id);
        }

        bool CurrentKernel() => !_disposed && _troops is { IsDisposed: false, IsCurrentKernel: true };

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

        // presenter 挂接(D-3' owner=原生部队实体;势力色 param 按挂接时点取,城陷易主
        // 由全量对账重挂收敛——同 M2.b 语义)。owner 实体缺席即类型化抛错(接缝漂移,
        // 不静默回落双实体路径)。
        void AttachMarker(SangoTroopMarkerPlacement placement)
        {
            if (_disposed || _presentersByTroopId.ContainsKey(placement.TroopId))
            {
                return;
            }

            Sango.Core.Troop? troop = Sango.Core.Scenario.Cur?.troopsSet.Get(placement.TroopId);
            if (troop == null)
            {
                return;
            }

            Entity owner = _troops.TroopEntityOrThrow(troop);
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
            _presentersByTroopId[placement.TroopId] = created[0];
        }

        void DetachMarker(int troopId)
        {
            if (!_presentersByTroopId.Remove(troopId, out Entity presenter))
            {
                return;
            }

            if (_world.IsAlive(presenter))
            {
                _presenterRuntime.Destroy(presenter);
            }
        }
    }
}
