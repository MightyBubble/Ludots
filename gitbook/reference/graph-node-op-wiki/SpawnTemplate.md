# 图内造兵

不用预置实体，阈值一到援军从图里长了出来。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_SpawnTemplate/poster.png" src="artifacts/evidence/capability_standard_graph_op_SpawnTemplate/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_SpawnTemplate/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 六种全可用（Effect / Score / Validation / Derived / Query / Script） |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | `source`（来源实体）、`a`（第一操作数）、`b`（第二操作数） |
| 特殊写法 | imm 填符号名（编译期解析） |

手册分册（全量字段与语义）：[算术与比较 · gr-op-02](../mod-editor-prd/config/gr-op-02-math.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/SpawnTemplate.json`）：

```json
{"id": "spawn", "op": "SpawnTemplate", "template": "GraphOps.Target"}
```

接线（值边把上一步的结果送进本节点端口）：

```json
{"from": "explicit", "fromPort": "value", "to": "spawn", "toPort": "source"}
{"from": "spawn_x", "fromPort": "value", "to": "spawn", "toPort": "a"}
```

## 这场是怎么搭出来的

上面的录像不是特效，是画廊里一张真实可跑的图（作者图 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/SpawnTemplate.json`，共 6 个节点）。照抄这张图，你就能在自家 mod 里得到同样的效果：

ConstFloat → ConstFloat → ConstFloat → **SpawnTemplate**（本篇） → LoadExplicitTarget → ModifyAttributeAdd

图跑完，字幕报出结果：

> 图内生成落地——援军入场，木桩血量{healthBefore}→{healthAfter}。

## 边界与更多用法

- 图种边界：六种图全都能用，不必为它挑图种。
- imm 是装载期解析的符号名：符号改名后，引用它的图要跟着改并重编译。
- 同类用法：伤害公式的缩放与浮动、斩杀线/格挡线这类阈值判断、把读数换算成另一个数。
## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_SpawnTemplate --adapter raylib
```
