// M3.b 地名标注:DefaultMap.bin 的 177 条河流/地名标注上图。文本走引擎正式 WorldHud
// 链路(presenters.json 的 WorldText behavior → PresenterAssetEmitRuntime.
// EmitWorldTextAsset → WorldHudToScreenSystem → Skia 屏幕层),中文经 text_locales.json
// 的 zh-CN 表(渲染侧系统字体 CJK);不自造文字渲染。每条标注一个 presenter 定义
// (WorldText 的 textToken 是定义级静态合同,177 定义=177 token),摆点数据在
// SangoTerrainMod assets/Presentation/map_labels.json(LabelExtract 从 bin 提取),
// 只动 presentation 层,内核 Game/ 不动。
// 已知阻塞(实测):WorldHud 投影门要求 sole possessed viewer(KnowledgeProjectionConsumer.
// TryResolveSoleLocalSeatViewer),而 sango 宿主图无 participants;运行时绑 seat.0 观察
// 席可过门,但会把顶层相机切到观察者地面视图(黑屏)且该图下 VirtualCameraBrain 缺位,
// 相机恢复请求直接抛"VirtualCameraRegistry is not configured"。故标注 presenter 已落地
// (265 个 world presenter 实证),文本可见性等引擎侧席位/相机基建补齐后自然点亮——
// 上层不代建相机基建。

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Modding;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Platform.Abstractions;

namespace Sango.Runtime
{
    public sealed record SangoMapLabelPlacement(
        int Index,
        string TokenKey,
        Vector3 PositionCm);

    public static class SangoMapLabels
    {
        public const string LabelsAssetUri = "SangoTerrainMod:assets/Presentation/map_labels.json";
        public const string DefinitionKeyPrefix = "sango.map.label.";

        /// <summary>
        /// 读标注摆点(VFS 提取产物):text/tokenKey/世界 cm 坐标。头部注释记录源坐标
        /// 约定(Unity x=东、z=北、1 单位=1 m,世界原点在图心)。
        /// </summary>
        public static List<SangoMapLabelPlacement> LoadPlacements(IVirtualFileSystem vfs)
        {
            if (vfs == null) throw new ArgumentNullException(nameof(vfs));
            System.IO.Stream? stream = null;
            try
            {
                stream = vfs.GetStream(LabelsAssetUri);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"SangoMapLabels asset missing: {LabelsAssetUri}", ex);
            }
            using JsonDocument document = JsonDocument.Parse(stream);
            var placements = new List<SangoMapLabelPlacement>();
            foreach (JsonElement row in document.RootElement.EnumerateArray())
            {
                int index = row.GetProperty("index").GetInt32();
                placements.Add(new SangoMapLabelPlacement(
                    index,
                    row.GetProperty("tokenKey").GetString() ?? throw new InvalidOperationException($"label {index} missing tokenKey"),
                    new Vector3(
                        row.GetProperty("worldXCm").GetInt32(),
                        row.GetProperty("worldYCm").GetInt32(),
                        row.GetProperty("worldZCm").GetInt32())));
            }

            if (placements.Count == 0)
            {
                throw new InvalidOperationException($"SangoMapLabels asset carried no placements: {LabelsAssetUri}");
            }

            return placements;
        }

        /// <summary>
        /// 逐条落地:每标注一个 owner 实体 + 对应 sango.map.label.{n} presenter 根。
        /// 定义未注册即抛错(fail-fast,不静默降级);返回落地条数。
        /// </summary>
        public static int Spawn(
            World world,
            PresenterEntityRuntime presenterRuntime,
            PresenterDefinitionRegistry definitions,
            PresentationStableIdAllocator stableIds,
            IReadOnlyList<SangoMapLabelPlacement> placements)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (presenterRuntime == null) throw new ArgumentNullException(nameof(presenterRuntime));
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            if (stableIds == null) throw new ArgumentNullException(nameof(stableIds));
            if (placements == null) throw new ArgumentNullException(nameof(placements));

            int spawned = 0;
            Entity[] owner = new Entity[1];
            int[] scopeId = new int[1];
            int[] presenterStableId = new int[1];
            VisualTransform[] transform = new VisualTransform[1];
            CullState[] cull = new CullState[1];
            Entity[] created = new Entity[1];
            foreach (SangoMapLabelPlacement placement in placements)
            {
                string key = $"{DefinitionKeyPrefix}{placement.Index}";
                if (placement.TokenKey != key)
                {
                    throw new InvalidOperationException($"SangoMapLabels token key mismatch: '{placement.TokenKey}' != '{key}'");
                }

                int definitionId = definitions.GetId(key);
                if (definitionId <= 0 || !definitions.TryGet(definitionId, out PresenterDefinition? definition))
                {
                    throw new InvalidOperationException($"Presenter definition '{key}' is not registered (SangoTerrainMod assets/Presentation/presenters.json).");
                }

                Entity anchor = world.Create(
                    new VisualTransform
                    {
                        Position = placement.PositionCm,
                        Rotation = Quaternion.Identity,
                        Scale = Vector3.One,
                    },
                    new CullState { IsVisible = true, LOD = LODLevel.High });
                owner[0] = anchor;
                scopeId[0] = placement.Index;
                presenterStableId[0] = stableIds.Allocate();
                transform[0] = world.Get<VisualTransform>(anchor);
                cull[0] = world.Get<CullState>(anchor);
                spawned += presenterRuntime.CreateEntityAnchoredRootBatch(
                    definitions,
                    definitionId,
                    owner,
                    scopeId,
                    presenterStableId,
                    transform,
                    cull,
                    definition,
                    created,
                    stableIds.Allocate);
            }

            return spawned;
        }
    }
}
