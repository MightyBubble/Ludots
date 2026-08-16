# fx-11 配置说明 · 堆叠

> 配置写法与行为。第一性需求见 [fx-11 PRD](../prd/fx-12-stack.md)；编辑器需求见 [UXD](../uxd/fx-12-stack.md)；现状见 [reference](../reference/fx-12-stack.md)。

## 1. 示例配置

champion 演示 mod 的标记 Buff（真实）：上限 1、刷新时长、满则拒新——

```json
[
  { "id": "Effect.Champion.Ezreal.EssenceFluxHit", "tags": ["Effect.Champion.Buff"],
    "presetType": "Buff", "lifetime": "After", "participatesInResponse": false,
    "duration": { "durationTicks": 240, "periodTicks": 0, "clockId": "FixedFrame" },
    "stack": { "limit": 1, "policy": "RefreshDuration", "overflowPolicy": "RejectNew" },
    "grantedTags": [ { "tag": "State.Champion.Ezreal.WMark", "formula": "Fixed", "amount": 1 } ] }
]
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `limit` | 层数上限（必填）；**须为正**——0/负值现状按无上限放行（陷阱，治理 E3） |
| `policy` | 三值必填：RefreshDuration 重置剩余时长 / AddDuration 累加 / KeepDuration 保持 |
| `overflowPolicy` | 两值必填：RejectNew 拒收 / RemoveOldest 挤最旧换新（层数不增） |

| 场景 | limit=1、policy=Refresh、overflow=RejectNew 下的结果 |
|---|---|
| 目标无此效果 | 新实体落地，层数=1 |
| 已有 1 层再施加 | 刷新剩余 240 tick，层数仍 1 |
| 换 overflow=RemoveOldest | 挤掉旧层换新（上限内等价"重上"） |

授予标签随层数差量增减（公式与回收细节见 fx-12）；图公式当前被 loader 拒绝，勿写。

## 3. 文件结构

`stack` 是效果模板顶层组件块（fx-01）；三字段全必填。即时寿命模板不参与合并（内联即完成）。

## 4. 运行时加载效果

loader 校验三字段必填；运行期提案时按模板身份在目标容器找同款效果：找到则按策略合并（作用于剩余时长，到期时点下帧重算），找不到则建新实体（首层=1）。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 三字段任一缺失 | 启动失败 |
| limit ≤0 | 现状放行为无上限（E3 治理后改为启动失败） |
| 授予标签差量失败 | 先回滚堆叠与效果实体，再上抛 |

## 6. 实例

- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/effects.json`（EssenceFluxHit）
- `mods/showcases/capability_standard/CapabilityStandardLiveSkillWorkbenchShowcaseMod/assets/GAS/effects.json`

**相关文档**：[fx-11 PRD](../prd/fx-12-stack.md) · [fx-03 配置说明](fx-04-lifetime.md) · [fx-12 配置说明](fx-13-granted-tags.md)
