# gr-op-04 配置说明 · 节点：属性与配置

> 配置写法与行为。第一性需求见 [gr-op-04 PRD](../prd/gr-op-04-attributes.md)；编辑器需求见 [UXD](../uxd/gr-op-04-attributes.md)；现状见 [reference](../reference/gr-op-04-attributes.md)。

## 1. 示例配置

节点画廊真实文件（`LoadAttribute.json` 与 `WriteSelfAttribute.json`）：

```json
[
  {
    "id": "showcase.graph_op.LoadAttribute",
    "kind": "Effect",
    "entry": "caster",
    "nodes": [
      { "id": "caster", "op": "LoadCaster" },
      { "id": "explicit", "op": "LoadExplicitTarget" },
      { "id": "loadHp", "op": "LoadAttribute", "attribute": "Health" }
    ],
    "controlEdges": [
      { "from": "caster", "fromPort": "next", "to": "explicit" },
      { "from": "explicit", "fromPort": "next", "to": "loadHp" }
    ],
    "valueEdges": [
      { "from": "explicit", "fromPort": "value", "to": "loadHp", "toPort": "source" }
    ]
  }
]
```

```json
[
  {
    "id": "showcase.graph_op.WriteSelfAttribute",
    "kind": "Effect",
    "entry": "heal",
    "nodes": [
      { "id": "heal", "op": "ConstFloat", "floatValue": 90 },
      { "id": "healSelf", "op": "WriteSelfAttribute", "attribute": "Health" }
    ],
    "controlEdges": [
      { "from": "heal", "fromPort": "next", "to": "healSelf" }
    ],
    "valueEdges": [
      { "from": "heal", "fromPort": "value", "to": "healSelf", "toPort": "value" }
    ]
  }
]
```

## 2. 逐 op 表

kind 缩写同 gr-op-01。

| op | 可用 kind | 输入引脚 | 输出 | 语义 |
|---|---|---|---|---|
| LoadAttribute | L+SC | source Entity + imm 属性名 | Float | 读 source 属性 Current |
| LoadSelfAttribute | L+SC | imm 属性名 | Float | 读图宿主自身属性 Current |
| WriteSelfAttribute | E+D | value Float + imm 属性名 | — | 直写自身属性 Current（绕过修改器） |
| LoadConfigFloat | L | imm 配置键 | Float | 读配置键的 Float 值 |
| LoadConfigInt | L | imm 配置键 | Int | 读配置键的 Int 值 |
| LoadConfigEffectId | L | imm 配置键 | Int | 读配置键解析出的效果 id |

互斥与陷阱：

- WriteSelfAttribute 是**唯一非 Effect 也非纯读**的可写事务 op（Effect 与 Derived 两类可用）：Derived 图回写自身、Effect 图直写自身走同一颗节点。它直写 SetCurrent，不建修改器——想被聚合管线管理就走效果修改器（fx-02），不要用这颗节点替代。
- LoadConfig 三件在监听宿主的图里禁用：监听图没有 owner 模板上下文可归属，编译拒绝。
- 属性名与配置键都是符号：属性上限与约束语义见 attr-01 与事实页；配置键经 ConfigKeyRegistry。

## 3. 文件结构

图文档放 `assets/GAS/graphs.json` 或分片目录；`attribute`/`configKey` 字段写符号名，见 gr-02。

## 4. 运行时加载效果

属性名与配置键在编译期分别经属性注册表与 ConfigKeyRegistry 解析；LoadConfig 绑定键后监听配置值；WriteSelfAttribute 编译为事务内直写指令。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 属性名未注册 | 编译失败，指明节点与属性名 |
| 配置键未注册 | 编译失败，指明节点与键名 |
| 监听图用 LoadConfig | 编译拒绝（无 owner 模板上下文） |
| 实体无该属性缓冲 | 读出缺省值，不报错 |

## 6. 实例

- `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/LoadAttribute.json`
- 同目录 `LoadSelfAttribute.json`、`WriteSelfAttribute.json`、`LoadConfigFloat.json`、`LoadConfigInt.json`、`LoadConfigEffectId.json`

**相关文档**：[gr-op-04 PRD](../prd/gr-op-04-attributes.md) · [attr-01 配置说明](attr-01-definition.md) · [gr-op-10 配置说明](gr-op-10-effect-actions.md)
