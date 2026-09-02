// M2.a 城池标记:把内核 citySet(Scenario.json 城市 x/y,256×256 格)投影成引擎世界
// 摆点,经引擎 presenter 管线(assets/Presentation/presenters.json 的 sango.city.marker,
// PresenterEntityRuntime.CreateEntityAnchoredRootBatch——MapLoader 模板批同一条正式路径)
// 渲染势力着色标记;不自造渲染。位置换算与 TerrainExport 同轴:内核格 x=北、y=东,
// 引擎世界以地图中心为原点(±256000cm),格边 GridSize 米;标记挂 SnapToGround 由
// CHTM 采样抬高,VisualTransform 的 Y 置 0 仅为锚点。

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
    public sealed record SangoCityMarkerPlacement(
        int CityId,
        string Name,
        Vector3 PositionCm,
        Vector4 ForceColor,
        int ForceId);

    public static class SangoCityMarkers
    {
        public const string PresenterDefinitionKey = "sango.city.marker";
        public const string ColorParamKey = "sango.city.marker.color";

        static readonly Vector4 UnownedColor = new(0.55f, 0.55f, 0.55f, 1f);

        /// <summary>
        /// 纯投影:citySet → (城 id、名称、世界 cm 摆点、势力色)。势力色取 Force.mFlag.color
        /// (旗帜表),无势力/无旗回落灰色;坐标换算见文件头。测试可 headless 断言。
        /// </summary>
        public static List<SangoCityMarkerPlacement> BuildPlacements(Sango.Core.Scenario scenario)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            Sango.Core.Map map = scenario.Map;
            if (map?.CellSet?.GetCell(0, 0) == null)
                throw new InvalidOperationException("SangoCityMarkers requires a loaded kernel map (real bin or synthetic grid).");

            float cellMeters = map.GridSize;
            int halfWorldCm = (int)(map.Width * cellMeters * 100f / 2f);
            var placements = new List<SangoCityMarkerPlacement>();

            scenario.citySet.ForEach(city =>
            {
                var positionCm = new Vector3(
                    (city.y * cellMeters + cellMeters * 0.5f) * 100f - halfWorldCm,
                    0f,
                    (city.x * cellMeters + cellMeters * 0.5f) * 100f - halfWorldCm);

                Sango.Core.Force? force = city.BelongForce > 0 ? scenario.forceSet.Get(city.BelongForce) : null;
                Vector4 color = UnownedColor;
                if (force?.mFlag != null)
                {
                    var flagColor = force.mFlag.color;
                    color = new Vector4(flagColor.r, flagColor.g, flagColor.b, 1f);
                }

                placements.Add(new SangoCityMarkerPlacement(city.Id, city.Name, positionCm, color, city.BelongForce));
            });

            return placements;
        }

        /// <summary>
        /// 按摆点批量落地:每城一个 owner 实体(VisualTransform/CullState),一个
        /// sango.city.marker presenter 根(scope=城 id,per-instance 势力色 param 覆盖)。
        /// 返回创建的标记数;定义未注册即抛错(fail-fast,不静默降级)。
        /// </summary>
        public static int Spawn(
            World world,
            PresenterEntityRuntime presenterRuntime,
            PresenterDefinitionRegistry definitions,
            PresentationStableIdAllocator stableIds,
            IReadOnlyList<SangoCityMarkerPlacement> placements)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (presenterRuntime == null) throw new ArgumentNullException(nameof(presenterRuntime));
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            if (stableIds == null) throw new ArgumentNullException(nameof(stableIds));
            if (placements == null) throw new ArgumentNullException(nameof(placements));
            if (placements.Count == 0) return 0;

            int definitionId = definitions.GetId(PresenterDefinitionKey);
            if (definitionId <= 0 || !definitions.TryGet(definitionId, out PresenterDefinition? definition))
                throw new InvalidOperationException(
                    $"Presenter definition '{PresenterDefinitionKey}' is not registered (assets/Presentation/presenters.json).");

            int count = placements.Count;
            var owners = new Entity[count];
            var scopeIds = new int[count];
            var presenterStableIds = new int[count];
            var transforms = new VisualTransform[count];
            var culls = new CullState[count];
            var colorOverrides = new ParamDefault[count][];

            for (int i = 0; i < count; i++)
            {
                SangoCityMarkerPlacement placement = placements[i];
                owners[i] = world.Create(
                    new VisualTransform
                    {
                        Position = placement.PositionCm,
                        Rotation = Quaternion.Identity,
                        Scale = Vector3.One,
                    },
                    new CullState { IsVisible = true, LOD = LODLevel.High });
                scopeIds[i] = placement.CityId;
                presenterStableIds[i] = stableIds.Allocate();
                transforms[i] = world.Get<VisualTransform>(owners[i]);
                culls[i] = world.Get<CullState>(owners[i]);
                colorOverrides[i] = new[]
                {
                    new ParamDefault
                    {
                        ParamKey = Ludots.Core.Presentation.Presenters.PresenterParamKeyRegistry.Register(ColorParamKey),
                        Lane = ParamLane.Vector,
                        VectorValue = placement.ForceColor,
                    },
                };
            }

            var created = new Entity[count];
            int spawned = presenterRuntime.CreateEntityAnchoredRootBatch(
                definitions,
                definitionId,
                owners,
                scopeIds,
                presenterStableIds,
                transforms,
                culls,
                definition,
                created,
                stableIds.Allocate,
                colorOverrides);
            return spawned;
        }
    }
}
