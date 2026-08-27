# MapField 作者手册

离散归属层（MapField / discreteId Field）是地图级栅格作者面：层声明、矩形笔画、地图挂载、过境通知、层级查询。引擎只认通用键与 id；省/郡等业务词只出现在 Mod 明文 key 里。

更深存储合同见 [Core Field2D](core-field2d.md)。本页是作者入口。

## 1. 声明层：`Fields/layers.json`

每个 discreteId 层一条：

```json
[
  {
    "id": "ownership.table",
    "kind": "discreteId",
    "cellSizeCm": 100,
    "chunkSizeCells": 8,
    "default": 0,
    "writerDomain": "map.field.ownership.table",
    "maxRegionIds": 16,
    "persistent": true
  }
]
```

- `id`：层 key，地图 `Fields.Layers` 与 `FieldTrackedCm.layer` 都引用它。
- `kind`：归属层用 `discreteId`。
- `cellSizeCm` / `chunkSizeCells`：世界 cm ↔ 格 / chunk。
- `maxRegionIds`：区域名表容量（含 default 0 之外的可编号区域）。
- Core + Mod 片段经 ConfigPipeline `ArrayById` 合并。

## 2. 笔画：`Fields/cells/<layerKey>.json`

大陆级作者只写矩形笔画；声明之外的任何键都会被装载器拒载：

```json
{
  "layer": "ownership.table",
  "regions": ["r1", "r2", "r3"],
  "rects": [
    [0, 0, 1, 1, 1],
    [3, 0, 4, 1, 2],
    [6, 0, 7, 1, 3]
  ]
}
```

- `regions[i]` → regionId `i+1`（1-based）。
- `rects` 每项 `[x0, y0, x1, y1, regionId]`，闭区间，装载直接 `FillRect`。
- 可选 `points`：散点补笔。
- FieldEditor / 存档写出走 `FieldRectCodec`（行 RLE + 纵向合并）。细节见 [Field Editor CLI](field-editor.md)。

## 3. 地图挂载：`Maps/<mapId>.json` → `Fields.Layers`

```json
{
  "Id": "field_layer_table",
  "Fields": {
    "Layers": ["ownership.table"]
  }
}
```

装载期 `GameEngine.CreateFieldsForSession`：

1. 按层 key 建 `FieldSessionStore` 并灌入 cells；
2. `FieldRegionMaterializer` 物化区域实体；
3. 读 `Fields/hierarchies.json`，`RegionHierarchyBuilder` 挂 `ChildOf` / `RegionGroupCm`。

实体跟踪加 `FieldTrackedCm: { "layer": "<layerKey>" }`。过境事件：`FieldRegionEntered` / `FieldRegionExited`（TriggerGraph `filters.region` 用区域明文 key）。

### 3.1 运行时重划：`FieldRegionRedraw`

玩法层运行时改写离散归属（重划县界、转移归属）走单一入口 `FieldRegionRedraw.ApplyDiscrete`：先注册批次内全部新区域 key（容量不足整批失败，不落半笔），再应用矩形笔画，随后补齐新区域实体、重算各区域占格、重建层级投影。驻留单位的成员关系由 chunk 变更戳在下一拍自动重估，进出事件照常走既有事件线。调试通道：`ludots.field.redraw`。

## 4. 层级：`Fields/hierarchies.json`

```json
[
  { "parent": "group.mid", "children": ["zone.a1", "zone.a2"] },
  { "parent": "group.top", "children": ["group.mid", "zone.b1"] }
]
```

- ArrayById，id 字段 = `parent`。
- 无格子的 parent 物化为 `RegionGroupCm`；查询用 `RegionHierarchyBuilder.TryResolveChain`（finest → ancestors）。
- 跨层区域 key 在同一张图必须唯一。

### 4.1 只读视觉投影与 mapmode

`FieldDiscreteVisualProjector` 始终读取最细 `DiscreteIdFieldLayerData`，把 leaf regionId 经装载期烘焙的 `ChildOf` remap 发布为 `GlobalFieldVisualKind.DiscreteOwnership`；它没有写投影格的 API。`Leaf` 显示作者 id，`AncestorDepth(n)` 取精确第 n 级祖先（该级不存在则为 0/透明），`GroupKey(key)` 只着色该 key 的后代。叶格变更由 `FieldDirtyCursor<int>` 按 chunk 增量发布；`RegionHierarchyRuntime.RebuildRemaps` 在 reparent/roster 变化后标脏受影响 leaf chunk。

Raylib 中地图变量 `mapmode=0/1/2` 分别选择 leaf / parent / grandparent；`field_hierarchy_query` 按 `M` 循环三档。byte palette 的最大投影 id 是 255；更大的 region/key 空间由 projector 的 `Vector4` palette callback 发布 RGBA，不截断 id。

## 5. Field-editor CLI

离线改 Mod 资产。命令与示例见 [Field Editor CLI](field-editor.md)。入口工程：`tools/FieldEditor/`。

## 6. Agent bridge：`ludots.field.*`

运行时探针（需已加载带 Fields 的地图）：

| Tool | 作用 |
|------|------|
| `ludots.field.layers` | 列层：key / kind / nonDefaultCount / regionCount |
| `ludots.field.cell` | 世界 cm 或 cell 坐标点查 regionId/key；有层级时附 `hierarchyChain` |
| `ludots.field.hierarchy` | 按 cell 或 region key 解析祖先链 |
| `ludots.field.redraw` | 运行时重划：按 regionKey 批量应用矩形笔画，返回注册数与改写格数 |

实现：`src/Libraries/Ludots.AgentBridge/Tools/FieldTools.cs`。

## 7. 标准 Showcases

| Binding | 焦点 |
|---------|------|
| `field_jing_yang_transit` | 两区过境 + 区内名单 + 面板 |
| `field_layer_table` | 三区表 + MapLoaded 计数面板 |
| `field_editor_paint` | field-editor 形状资产（paint.a / paint.b）+ 过境 |
| `field_hierarchy_query` | hierarchies.json + `TryResolveChain` |

`field_jing_yang_transit` 的“荆域火计”把区域状态投影为地图变量，`activationPrecondition` 的 Validation graph 通过纯 `InvokeScript` 读取 `region_code` 并 fail closed；施放成功后，`EventSignal` 经既有 GAS → TriggerGraph bridge 更新 `fire_cast_count`。这是 `HasTag` scope-effect 组合在当前 TriggerGraph 不允许 `ApplyEffectTemplate` / `RemoveEffectTemplate` 时的无 fallback 数据组合。

目录均在 `mods/showcases/field_*/`；数据-only（无 csproj）。启动：

```powershell
.\scripts\run-mod-launcher.cmd cli launch '$field_layer_table' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch 'preset:field_hierarchy_query_raylib'
```

验收索引：`scripts/acceptance/acceptance.index.json`；能力表：[Capability Standard Showcases](capability-standard-showcases.md)。
