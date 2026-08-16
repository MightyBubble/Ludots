# rt-04 配置说明 · 表现事件

> 配置写法与行为。第一性需求见 [rt-04 PRD](../prd/rt-04-presentation.md)；编辑器需求见 [UXD](../uxd/rt-04-presentation.md)；现状见 [reference](../reference/rt-04-presentation.md)。

## 1. 示例配置

容量在 `game.json` 的 presentation 块（核心 mod 基线，`mods/LudotsCoreMod/assets/game.json` 现状片段）：

```json
{
  "presentation": {
    "gasPresentationEventCapacity": 65536
  }
}
```

## 2. 作者可配什么与在哪配

| 想控制什么 | 在哪 | 说明 |
|---|---|---|
| 事件缓冲容量 | `game.json` `presentation.gasPresentationEventCapacity` | 每 tick 缓冲大小；必 >0，满即抛错（配置错误）——大量粒子级事件才需要动它 |
| 发不发某事件 | 无开关 | 事件由施法/效果管线自动发（九种转折一一对应）；要少事件就少配置对应的技能/效果行为 |

九种事件与失败原因（自动发布，无需声明）：

| 事件 | 触发时机 |
|---|---|
| CastStarted / CastFailed / CastCommitted / CastFinished / CastInterrupted | 施法开始/被拒（携七值失败原因枚举）/提交/正常完成/被打断 |
| EffectApplied / EffectActivated / EffectExpired / EffectCancelled | 效果应用/激活（含周期再激活）/到期/取消 |

## 3. 文件结构

`assets/game.json`（引擎/核心 mod 基线，mod 可深合并覆盖容量字段，见 cfg-06）。表现事件本身无配置文件。

## 4. 运行时加载效果

引擎装配期按 presentation 配置构造每 tick 事件缓冲并注册为服务；固定 tick 内 GAS 管线在九种转折处写入；表现投影系统消费后清除表现标志。效果事务失败时缓冲支持按事务撤销已写事件。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 容量 ≤0 或缺失非法值 | 启动失败 |
| 缓冲满 | 抛错（"overflow is a configuration error"）——改容量或减事件量，无静默丢弃 |

## 6. 实例

- 容量基线：`mods/LudotsCoreMod/assets/game.json`（presentation 块）
- 消费方示例：表现投影系统（见 reference）

**相关文档**：[rt-04 PRD](../prd/rt-04-presentation.md) · [cfg-06 配置说明](cfg-06-game-config.md) · [pres-01](pres-01-performers.md)
