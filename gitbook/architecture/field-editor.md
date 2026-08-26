# Field Editor

`tools/FieldEditor` 是独立的离线作者工具，提供 Raylib 画布和 CLI：对 Mod 目录下的 `Fields/layers.json` 与 `Fields/cells/<layerKey>.json` 做声明、区域登记与矩形笔画。写出格式与引擎装载格式一致——**schemaVersion 2 + `regions` + `rects`**（可选 `points`），禁止 v1 的 `cells` 数组。编辑器内存态直接使用 `ChunkedField2D<int>`；读取 rect 时调用 `FillRect`，保存时从场合并 rect，不展开成逐格字典或 point 列表。

运行（在仓库根，已还原 .NET 8）：

```powershell
dotnet run --project tools/FieldEditor -- <command> --mod <ModAssetsOrModRoot>
```

`--mod` 指向含 `assets/Fields/` 的目录（通常是 `.../SomeMod` 或 `.../SomeMod/assets`；工具按 `Fields/layers.json` 相对路径解析）。

## 命令一览

| 命令 | 作用 |
|------|------|
| `layers` | 列出已声明层 |
| `new-layer` | 追加一条 discreteId 层到 `layers.json` |
| `regions` | 列出某层区域 key 及占格数 |
| `regions-add` / `regions-remove` / `regions-rename` | 增删改区域 key（删区会清其格） |
| `regions-color` / `colors` | 设置 `#RRGGBB` 区域色 / 列出区域色 |
| `cell` | 读/写/擦单格（`--at x,y`，写用 `--key`，擦用 `--erase`） |
| `pick` / `eyedrop` | 读取一格并把非空区域设为活动画笔 key |
| `rect` | 用显式或活动画笔 key 填充闭区间矩形并立刻写出 |
| `brush` | `--at x,y --radius N` 绘制 cell 空间的 Chebyshev 半径方形画笔 |
| `erase` | 擦除矩形（回到 default） |
| `undo` / `redo` | 从磁盘 sidecar 撤销/重做，可跨 CLI 进程 |
| `render` | ASCII 预览当前笔画 |
| `save` | 再校验容量并写出（突变命令本身已 atomic 写出） |
| `session` | 保持进程存活，逐行执行子命令，`quit` 退出 |
| `canvas` | 打开 Raylib 可视化画布；仅按 `S` 时写出 cells 资产 |

`new-layer` 可选 `--map <mapId>`：把层 id 写入 `Maps/<mapId>.json` 的 `Fields.Layers`。

`pick` 选中的活动 key 与 undo/redo 栈保存在同目录的 `<layer>.field-editor-history.json`。进入交互模式后，子命令自动继承 `session --mod ... --layer ...`：

```text
dotnet run --project tools/FieldEditor -- session --mod %MOD% --layer ownership.paint
field-editor> pick --at 1,1
field-editor> brush --at 8,8 --radius 2
field-editor> undo
field-editor> quit
```

## Raylib 画布

图形环境中运行：

```powershell
dotnet run --project tools/FieldEditor -- canvas --mod %MOD% --layer ownership.paint
```

左侧区域面板显示 `regions` 容量、区域 key 与颜色。颜色优先读取 `<layer>.field-editor-meta.json` 的 `regionColors`；未配置颜色的 key 使用稳定的派生色。单击区域可选择活动 key，区域较多时在面板内滚轮滚动。

| 输入 | 操作 |
|------|------|
| `1` | 单格画笔，可按住左键拖画 |
| `2` | Chebyshev 方形画笔；`[` / `]` 调整半径 |
| `3` | 左键按下并拖到终点后填充矩形 |
| `4` | 擦除，可按住左键拖动 |
| `5` | 滴管；单击非空格选择其区域 key |
| 鼠标滚轮 | 在画布缩放；在区域面板滚动 |
| 鼠标中键或右键拖动 | 平移画布 |
| `Z` / `Y` | 撤销 / 重做 |
| `S` | 校验容量并原子保存 cells 资产 |
| `Esc` | 退出；存在未保存 cells 改动时拒绝退出并提示先按 `S` |

画布与 CLI 共用 `CellsDocument`、history sidecar 和 metadata sidecar，不另设资产 codec。Linux 下没有 `DISPLAY` 或 `WAYLAND_DISPLAY` 时，`canvas` 会明确报错并返回失败，不会静默跳过。

`FieldCellsConfig` 使用 fail-strict JSON，不能在引擎 cells JSON 中加入未知字段。因此区域色只写入编辑器 sidecar `<layer>.field-editor-meta.json` 的 `regionColors`，引擎资产保持原 schema。

## 示例：从空 Mod 画出两色地

以下与 showcase `field_editor_paint` 同形（key 为 `paint.a` / `paint.b`）。

```powershell
set MOD=mods/showcases/field_editor_paint/FieldEditorPaintMod

dotnet run --project tools/FieldEditor -- layers --mod %MOD%

dotnet run --project tools/FieldEditor -- new-layer --mod %MOD% ^
  --id ownership.paint --cell-size 100 --chunk 8 --max-regions 16 --writer map.field.ownership.paint

dotnet run --project tools/FieldEditor -- regions-add --mod %MOD% --layer ownership.paint --key paint.a
dotnet run --project tools/FieldEditor -- regions-add --mod %MOD% --layer ownership.paint --key paint.b

dotnet run --project tools/FieldEditor -- rect --mod %MOD% --layer ownership.paint ^
  --key paint.a --from 0,0 --to 3,3
dotnet run --project tools/FieldEditor -- rect --mod %MOD% --layer ownership.paint ^
  --key paint.b --from 6,0 --to 9,3

dotnet run --project tools/FieldEditor -- render --mod %MOD% --layer ownership.paint
dotnet run --project tools/FieldEditor -- save --mod %MOD% --layer ownership.paint
```

写出后 `assets/Fields/cells/ownership.paint.json` 形如：

```json
{
  "schemaVersion": 2,
  "layer": "ownership.paint",
  "regions": ["paint.a", "paint.b"],
  "rects": [
    [0, 0, 3, 3, 1],
    [6, 0, 9, 3, 2]
  ]
}
```

地图侧只需 `Fields.Layers: ["ownership.paint"]`，并给英雄挂 `FieldTrackedCm`。完整挂载与过境见 [MapField 作者手册](mapfield-howto.md)。

## 约束

- 只作者 **discreteId** 层；scalar / vector 场不走本 CLI。
- 区域 key 与层 key 都是 Mod 明文；引擎不做业务词解释。
- `maxRegionIds` 超限时 `save` / 突变写出失败关闭。
- 擦除与重画后磁盘始终是压缩 rect 集，不是百万三元组。
- history / metadata sidecar 解析失败会显式报错，不会回退为空状态。

## 相关

- 作者总览：[MapField 作者手册](mapfield-howto.md)
- 运行时存储：[Core Field2D](core-field2d.md)
- 验收展厅：`field_editor_paint`（`FieldEditorPaintAcceptanceTests`）
