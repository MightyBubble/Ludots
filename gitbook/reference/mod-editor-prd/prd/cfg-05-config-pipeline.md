# cfg-05 · 配置管线与跨 mod 合并

> 产品承诺 · 已冻结。理想实现见 [cfg-05 spec](../spec-runtime/cfg-05-config-pipeline.md)；现状见 [cfg-05 reference](../reference/cfg-05-config-pipeline.md)。

## 1. 定位

你的 mod 里每个 JSON 配置文件，都会和引擎默认配置、其他 mod 的同名文件合并成一份再进入引擎。这一篇定义合并的规则：写法 → 谁生效。案例速查见 cfg-07。

## 2. 示例配置

一次完整的覆盖合并，三段对照。示例借用效果表的条目形状——表怎么声明与加载见 cfg-04，效果表的字段合同见 fx-02。上游（引擎默认或别的 mod）有这样一条效果：

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

合并结果（你后加载，赢下写到的字段；数组是整组替换，所以 grantedTags 整组换成你的版本）：

```json
[ { "id": "Effect.Example.Income",
    "presetType": "Buff", "lifetime": "Infinite",
    "grantedTags": [ { "tag": "State.Example.Income", "formula": "Fixed", "amount": 5 } ] } ]
```

屏蔽别家条目则只需一行：`[ { "id": "Effect.SomeMod.Something", "__delete": true } ]`。

## 3. 规则与效果

| 规则 | 写法 | 效果 |
|---|---|---|
| 顺序事实来源 | 改启动计划的选择器与依赖声明（cfg-03） | 加载顺序在生成期烘焙，运行期不可调；两个无依赖 mod 的胜负即计划中的先后 |
| 同 id 深合并 | 同 id 条目只写想改的字段 | 后加载者赢，且只赢写到的字段；对象递归合并；数组整体替换；条目顺序 = id 首次出现序 |
| 覆盖一个标量 | 同 id + 该字段 | 赢该字段，其余保留 |
| 扩展对象子路径 | 同 id + 只写子路径（如 `phaseGraphs.OnPeriod.post`） | 递归合并到叶子 |
| 屏蔽条目 | 同 id + `"__delete": true` | 时序删除：只删此前加载的该 id；更晚的 mod 写同名会复活 |
| 命名空间 | 全局扁平 | 所有配置 id 跨 mod 共享一个命名空间，靠前缀分层；撞名处理当前不一致（技能后者覆盖、效果报错），治理方向待评审 |
| 大小写 | 一律敏感 | 路径、策略名、id 全部区分大小写 |

## 4. 文件结构

| 来源 | 位置 | 说明 |
|---|---|---|
| 引擎默认 | `Core:Configs/` 下的同名文件 | 永远最先加载，是所有 mod 的共同基底 |
| 你的 mod | `{你的mod名}:assets/GAS/effects.json` | 把 assets 当统一内容根的放法 |
| 你的 mod | `{你的mod名}:assets/Configs/GAS/effects.json` | 与引擎默认目录同构的放法 |

mod 内两个位置任选其一；两处并存时 Configs 里的后生效。新配置文件类型必须先在配置目录登记（cfg-04），属治理审批。

## 5. 运行时加载效果

启动时按这条链一次走完：

1. **收集**：按候选地址逐来源收片段——引擎默认 → 各 mod 按计划顺序 → mod 内 assets/ 先、Configs/ 后；登记了分片目录的表，各来源先收主文件、再按稳定顺序收分片（分片即普通片段，同样受覆盖规则裁决）。
2. **合并**：按配置目录登记的策略合并成一份（同 id 深合并为主）。
3. **编译**：各配置类型的加载器校验字段、解析引用，编译进对应注册表。
4. **消费**：mod 装配（含扩展注册，cfg-01 第 5 节）先于本链发生，注册的扩展键供编译期引用；此后运行期读到的注册表永远是合并后的最终结果。

此后运行期不再变化：重载是预留接口、当前无调用方；重进地图不重新合并。覆盖是字段级且持久的——你写过的字段持续压住上游同字段的后续修改，上游升级不等于覆盖自动让位。

## 6. 预期反馈

- **启动期**：合并与编译的成败一次定形；坏文件、坏引用在启动期报出。
- **编辑器内**：合并预览展示参与片段、每条目的最终赢家、最终生效顺序（= 计划顺序），并标出被下游覆盖的字段。

## 7. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 文件不存在 | 静默跳过——mod 只写自己需要的文件是正常用法 |
| JSON 语法错误 | 启动失败，指出坏文件 |
| 条目缺 id 或 id 非字符串 | 启动失败 |
| 文件路径未在配置目录登记 | 启动失败（没登记的 JSON 不是配置） |
| 引用不存在的 id（如模板引用） | 启动失败，指明引用方与目标 |
| 依赖缺失或版本不符 | 启动失败（见 cfg-01） |

## 8. 编辑器要点

- **位置自由**：保存跟随该 mod 已有放法；无任何配置文件时给默认位置并允许改。
- **合并预览**是"为什么我的值没生效"的第一诊断入口；最终生效顺序直接展示计划顺序。
- **被下游覆盖提示**与**依赖更新覆盖审计**（升级上游前 diff 下游覆盖点，类似包管理器 why / outdated）。
- **删除两义分开**：删自己拥有的行是物理删除；屏蔽他方是写 `__delete`。
- 热应用级别：本层全部内容为重启级。

## 9. 实例

- `assets/` 根放法：`mods/LudotsCoreMod/assets/GAS/order_types.json`
- `assets/Configs/` 放法：`mods/showcases/moba_demo/MobaDemoMod/assets/Configs/GAS/graphs.json`
- 依赖决定顺序：`mods/showcases/moba_demo/MobaDemoMod/mod.json`（依赖两个基础 mod，闭包后排其后、可覆盖）

**相关文档**：[cfg-05 spec](../spec-runtime/cfg-05-config-pipeline.md) · [cfg-05 reference](../reference/cfg-05-config-pipeline.md) · [cfg-03](cfg-03-launch-graph.md)（顺序事实来源）· [cfg-04](cfg-04-config-tables.md)（策略登记）· [cfg-07](cfg-07-merge-rules.md)（案例速查）
