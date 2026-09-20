# rt-01 配置说明 · 时钟系统

> 配置写法与行为。第一性需求见 [rt-01 PRD](../prd/rt-01-clocks.md)；编辑器需求见 [UXD](../uxd/rt-01-clocks.md)；现状见 [reference](../reference/rt-01-clocks.md)。

## 1. 示例配置

GAS 步时钟（`GAS/clock.json`，引擎现状全量）：

```json
{
  "mode": "Auto",
  "stepEveryFixedTicks": 1
}
```

引擎固定帧率（`Engine/clock.json`，引擎现状全量）：

```json
{
  "FixedHz": 20
}
```

## 2. 字段与行为

| 字段 | 所在 | 这样配会产生什么效果 |
|---|---|---|
| `mode` | `GAS/clock.json` | `Auto` 每固定 tick 自动走步；`Manual` 只走请求步；`Paused` 停走。两字段都**显式必填**，缺一即启动失败 |
| `stepEveryFixedTicks` | `GAS/clock.json` | 每 N 个固定 tick 一个步进（≥1）；步进速率 Hz=FixedHz÷N。效果 duration ticks 与周期以此计 |
| `FixedHz` | `Engine/clock.json` | 固定帧率；1 固定 tick=1000÷FixedHz 毫秒。引擎级，非 mod 作者常规改动面 |
| `time.scale_permille` | 实体模板 `AttributeBuffer` | 实体本地变速（1000=同步，0=冻结本地；上限见事实页引擎常量侧 spec），整数必填、非法即运行失败 |

步进速度千分比（scalePermille）是运行时策略参数，无配置文件入口，仅经引擎/工具在运行中设置。

## 3. 文件结构

`assets/GAS/clock.json`（目录登记、DeepObject 合并，可被 mod 覆盖字段）；`assets/Engine/clock.json`（引擎域）；实体变速写在实体模板组件（见 ent-01）。

## 4. 运行时加载效果

时钟表加载并构造步进策略；步进速率在引擎装配期按 FixedHz÷stepEveryFixedTicks 换算并注入下游消费者；`time.scale_permille` 属性随约束链注册。每固定 tick：推进固定帧 → 按策略消费步进 → 实体本地系统按本地千分比累加本地步。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| clock.json 缺 mode/stepEveryFixedTicks | 启动失败，指明字段 |
| mode 非法拼写、stepEveryFixedTicks < 1 | 启动失败 |
| 实体缺 AttributeBuffer | 运行失败，指明实体 |
| scale_permille 非有限/非整数千分比/负/超上限 | 运行失败，指明属性名与原因 |

## 6. 实例

- 引擎默认：`assets/GAS/clock.json`、`assets/Engine/clock.json`
- 实体变速：实体模板 `AttributeBuffer` 的 `time.scale_permille`（见 ent-01）

**相关文档**：[rt-01 PRD](../prd/rt-01-clocks.md) · [ent-01 配置说明](ent-01-templates.md) · [UXD](../uxd/rt-01-clocks.md)
