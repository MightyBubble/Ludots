# Diagram Specs — `diagram_forge` 引擎使用说明

`scripts/diagram_forge.py` 是 Ludots 文档架构图的统一重绘引擎：**一张图 = 一个 JSON spec**。
本目录每个 `*.json` 对应 `docs/diagrams/<name>.svg` + `docs/diagrams/<name>.png`。

## 渲染命令

```bash
# 渲染单张
python scripts/diagram_forge.py docs/diagrams/specs/<name>.json

# 渲染本目录全部 spec
python scripts/diagram_forge.py --all

# 可选参数
--specs-dir <dir>   # 覆盖 spec 目录
--out-dir <dir>     # 覆盖输出目录（默认 docs/diagrams）
```

输出固定为 SVG（`svg.fonttype='none'`，真 `<text>` 元素，可改字体）+ PNG（dpi=180）。
渲染结束打印 `canvas=宽x高, nodes=N, edges=M, groups=G`。

**自检流程**：渲染后引擎自动做重叠检测（节点×节点、图例×节点/分组、画布溢出），
命中即打印 `[diagram_forge WARNING]`。警告不等于失败，但交付前应清零。
建议再用 ReadMediaFile 查看 PNG 做视觉确认。

## Spec 顶层结构

```jsonc
{
  "name": "engine-architecture",      // 缺省取文件名；输出 <name>.svg/.png
  "title": "Ludots Engine Architecture",
  "subtitle": "Hexagonal ECS + Everything-is-Mod",
  "canvas": { "width": 1720, "height": 1330 },   // 画布单位 ≈ px；100 单位 = 1 英寸
  "theme": { ... },                   // 可选，覆盖设计 token（见下文）
  "legend": { ... },                  // 可选，图例面板（固定左上，自动预留边距）
  "groups": [ ... ],                  // 可选，分组/泳道容器（虚线或实线大圆角框）
  "nodes": [ ... ],                   // 圆角盒节点
  "layout": [ ... ],                  // 可选，布局助手（grid / radial）
  "edges": [ ... ],                   // 连线
  "notes": [ ... ]                    // 可选，自由注释文本
}
```

坐标系：**y 向下**（CSS 风格），`x,y` 均为元素**左上角**。
布局助手只为「没有显式 x/y」的节点分配位置；显式坐标永远优先，可与助手混用。

## nodes — 圆角盒节点

```jsonc
{
  "id": "gas",                        // 必填，edges 引用它
  "title": "GAS",
  "items": ["Ability · Effect · Attribute", "Tag · Cue · Target"],  // 条目行，垂直居中
  "chips": ["Tick", "Pacemaker"],     // 可选，内部小芯片（白底小圆角块）
  "chip_columns": 3,                  // chips 每行个数，默认 3
  "x": 120, "y": 545, "w": 300, "h": 150,   // 可省略 → 由 layout 助手定位 / 自动测量尺寸
  "tint": "sage",                     // 预设低饱和配色（见下表），或手动给:
  "fill": "#f8f9fb", "border": "#dfe3e9",   // 手动色（tint 优先）
  "border_w": 1.6,                    // 默认 1.0；中心节点可加重
  "valign": "center"                  // 内容块垂直对齐，默认 center，可选 "top"
}
```

- `w/h` 省略时自动按文字估算（CJK 按 1em、拉丁按 0.56em）。
- 内容块（标题 + chips + items）整体垂直居中，不会顶头留白。
- 未定位（无 x/y 且未被任何 layout 引用）的节点会落到内容原点并触发 WARNING。

### 预设 tint（低饱和灰阶/石板色系，图例与节点共用）

`core`（中心，深石板边框）· `slate` · `steel` · `sage` · `stone` · `sand` ·
`mauve` · `clay` · `teal` · `graphite`
全部为浅底 + 柔和描边，禁止蓝紫渐变与高饱和。

## groups — 分组/泳道容器

```jsonc
{ "id": "platforms", "title": "Platform Adapters",
  "x": 700, "y": 1160, "w": 520, "h": 130,
  "tint": "graphite",                 // 可选；不给则用默认浅灰填充
  "border_style": "dashed",           // "dashed"（默认）| "solid"
  "title_anchor": "top-left" }        // 或 "top-center"
```

分组绘制在节点之下；`edges` 的 `from/to` 也可以引用分组 id（连线自动裁剪到边框）。

## edges — 连线

```jsonc
{ "from": "engine", "to": "gas",
  "route": "straight",                // straight（默认）| elbow | curve
  "style": "solid",                   // solid（默认）| dashed
  "label": "optional label",          // 白底小标签；elbow 自动放在最长段中点
  "arrow": false,                     // 默认 true（小三角箭头）
  "color": "#8a94a3", "width": 1.2,   // 可选覆盖
  // 仅 elbow:
  "from_side": "right", "to_side": "left",   // top/bottom/left/right，决定出入锚点
  "stub": 30,                         // 出盒后的直行段长度
  // 仅 curve:
  "rad": 0.18 }                       // 弧度，正负控制弯曲方向
```

- **straight**：中心对中心直线，两端自动裁剪到矩形边框——辐射图首选，最干净。
- **elbow**：正交肘形路由（stub → 折线 → stub），流程图/网格流首选。
  目前不做全图障碍避让；请用合理的 `from_side/to_side` 让路径绕开其他节点。
- **curve**：arc3 贝塞尔曲线，适合表达回环/反馈边。

## layout — 布局助手（两种）

### (a) grid 行列网格流

```jsonc
{ "type": "grid", "nodes": ["n1", "n2", "n3", "n4"],
  "origin": [90, 610], "columns": 3,
  "spacing_x": 30, "spacing_y": 40,
  "cell_w": 300, "cell_h": 120 }      // 可选；缺省取节点自动尺寸的最大值
```

按 nodes 顺序行优先排布。配合 `elbow` 边即得正交流程图。

### (b) radial 中心辐射

```jsonc
{ "type": "radial", "nodes": ["s1", "s2", "s3", "s4"],
  "center": [880, 300],               // 可选；缺省 = 内容区中心（自动避开图例区）
  "radius_x": 330, "radius_y": 170,
  "start_angle": 90,                  // 度，90 = 正上方
  "direction": "cw",                  // cw（默认）| ccw
  "angles": [112.5, 67.5, 202.5] }    // 可选，逐节点精确指定角度
```

注意：**中心节点本身不参与 radial**，需自己给 x/y（中心坐标减半宽半高）。

## legend — 图例

```jsonc
{ "title": "Legend",
  "x": 40, "y": 130,                  // 默认左上 (40, 128)
  "width": 250,                       // 可选；缺省按最长标签自动
  "row_height": 33,
  "items": [
    { "label": "Core Engine", "tint": "sage" },                  // 色块项
    { "label": "虚线依赖", "style": "line", "line": "dashed", "color": "#8a94a3" }  // 线型项
  ] }
```

图例在布局前完成测量：radial 缺省中心会自动右移避开图例区；
引擎同时做图例×节点/分组重叠检测，命中即 WARNING。**图例永远不得压内容。**

## notes — 注释

```jsonc
{ "text": "smoke test note", "x": 60, "y": 960, "ha": "left" }
```

## 设计 token（theme 可覆盖）

| token | 默认 | 用途 |
|---|---|---|
| `canvas_bg` | `#ffffff` | 白底 |
| `node_fill` / `node_border` | `#f8f9fb` / `#dfe3e9` | 默认盒填充/边框 |
| `corner_radius` | `8` | 圆角 |
| `title_color` / `title_size` | `#181b20` / `20` | 大标题（700） |
| `subtitle_color` / `subtitle_size` | `#5b6470` / `12` | 副标题 |
| `node_title_size` / `node_item_size` | `12.5` / `10.2` | 节点文字 |
| `edge_color` / `edge_w` | `#8a94a3` / `1.2` | 连线 |
| `padding` | `40` | 画布边距 |

字体：依次尝试 Microsoft YaHei → SimHei → Noto Sans CJK（含直接注册系统字体文件），
全部失败会显式 WARNING 并回退 DejaVu Sans。无投影、无渐变。

## 转录工作流建议（给 transcriber）

1. 看旧 PNG，把内容忠实抄成 spec（节点=盒、泳道=group、箭头=edge）。
2. 结构简单的流程图用 `grid` + `elbow`；中心辐射图用 `radial` + `straight`；
   复杂图先给绝对坐标，再微调。
3. 渲染 → 看 WARNING（重叠/溢出全部修掉）→ ReadMediaFile 看 PNG。
4. 迭代到无警告、无视觉重叠后交付。

参照样张：`engine-architecture.json`（radial 手写坐标版 + legend + group + chips）。

## 已知限制

- elbow 路由不做全图自动避障；复杂交叉需手写锚点（`from_side/to_side`）或绝对坐标。
- 文本宽度为估算（CJK 1em / 拉丁 0.56em），超长单词可能轻微溢出——给足 `w` 即可。
- 节点不支持富文本/多列 items；超长清单请拆节点或换行。
- 无自动换行；`items` 每行是一条独立文字。
