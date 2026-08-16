# cfg-05 配置说明 · 配置管线与跨 mod 合并

> 配置写法与行为。第一性需求见 [cfg-05 PRD](../prd/cfg-05-config-pipeline.md)；编辑器需求见 [UXD](../uxd/cfg-05-config-pipeline.md)；现状见 [reference](../reference/cfg-05-config-pipeline.md)。

## 1. 示例配置

一次完整覆盖合并，三段对照（教学骨架，非仓库文件；真实合并实例见合并预览或 rts 演示）。上游有这样一条效果：

```json
[ { "id": "Effect.Example.Income",
    "presetType": "Buff", "lifetime": "Infinite",
    "grantedTags": [ { "tag": "State.Example.Income", "formula": "Fixed", "amount": 2 } ] } ]
```

你的 mod 只写想改的字段：

```json
[ { "id": "Effect.Example.Income",
    "grantedTags": [ { "tag": "State.Example.Income", "formula": "Fixed", "amount": 5 } ] } ]
```

合并结果（你赢写到的字段；数组整组替换，grantedTags 换成你的版本；presetType 与 lifetime 保留上游值）。屏蔽一行：`[ { "id": "Effect.SomeMod.Something", "__delete": true } ]`。

## 2. 规则与行为

| 规则 | 写法 | 效果 |
|---|---|---|
| 顺序事实来源 | 改计划的选择器与依赖声明（cfg-03） | 顺序生成期烘焙；两个无依赖 mod 的胜负即计划先后 |
| 同 id 深合并 | 同 id 只写想改的字段 | 后到者赢且只赢写字段；对象递归；数组整组替换；条目序 = id 首现序 |
| 覆盖标量 | 同 id + 该字段 | 赢该字段，其余保留 |
| 扩展对象子路径 | 同 id + 只写子路径 | 递归合并到叶子 |
| 屏蔽条目 | 同 id + `"__delete": true` | 时序删除：只删此前加载的该 id；更晚 mod 写同名会复活 |
| 命名空间 | 全局扁平 | 撞名处理当前不一致（技能后者覆盖、效果报错），治理待评审 |
| 大小写 | 一律敏感 | 路径、策略名、id 全部区分 |

## 3. 文件结构

| 来源 | 位置 |
|---|---|
| 引擎默认 | assets/ 根（`Core:`）下同名文件，永远最先加载 |
| 你的 mod | `{mod名}:assets/` 下同名文件（唯一位置） |
| 分片 | 登记了分片目录的表可一条一文件（cfg-04） |

新表类型须先登记（cfg-04），属治理审批。

## 4. 运行时加载效果

启动一次走完：**收集**（引擎默认 assets/ 根 → 各 mod 按计划顺序、各自 assets/ 根；含分片：先主文件后同根分片）→ **合并**（按登记策略）→ **编译**（加载器校验、解析引用）→ **注册进表**。此后运行期不变；mod 装配（含扩展注册）先于本链，注册的扩展键供编译期引用。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 文件不存在 | 静默跳过 |
| JSON 语法错误 | 启动失败，指出坏文件 |
| 条目缺 id / 未登记路径 | 启动失败 |
| 引用不存在的 id | 启动失败，指明引用方与目标 |
| 依赖缺失或版本不符 | 启动失败（cfg-01） |

## 6. 实例

- 引擎默认：`assets/GAS/graphs.json`（Core 根）
- mod 追加：`mods/showcases/moba_demo/MobaDemoMod/assets/GAS/graphs.json`

**相关文档**：[cfg-05 PRD](../prd/cfg-05-config-pipeline.md) · [cfg-03 配置说明](cfg-03-launch-graph.md) · [cfg-07 配置说明](cfg-07-merge-rules.md)
