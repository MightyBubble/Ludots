# ed-01 配置说明 · 实时技能工作台编辑基座

> 配置写法与行为。第一性需求见 [ed-01 PRD](../prd/ed-01-workbench-base.md)；编辑器需求见 [UXD](../uxd/ed-01-workbench-base.md)；现状见 [reference](../reference/ed-01-workbench-base.md)。

## 1. 示例配置

工作台是引擎内的 capability mod，无 mod 作者配置文件。与其相关的配置只有挂载与保存目标（引擎侧装配，教学骨架）：

```text
mods/capabilities/live_skill_workbench/   ← 工作台能力 mod 本体（挂载即启用）
保存目标：Showcase mod 挂载后取其根（默认保存目标为能力演示 mod）
```

## 2. 作者可配什么与在哪配

| 想控制什么 | 在哪 | 说明 |
|---|---|---|
| 工作台是否可用 | 启动计划是否挂载该 capability mod（cfg-03） | 不挂载即无此能力，游戏零开销 |
| 保存落到哪个 mod | 保存目标 mod 的挂载（保存命令带目标） | 目标未配置时保存命令 fail-closed |
| 被编辑的内容 | effects/graphs/tag_rules/约束等常规表（各卷） | 工作台不引入新 schema——编辑对象就是既有配置 |

规则表——DataPlane 命令面（作者不直接写，工具消费）：

| 项 | 值 |
|---|---|
| 主题 | `ludots.capability.liveSkillWorkbench.session` |
| 命令 11 个 | 暂存/丢弃/选目录项/预检/应用到下次施放/立即属性命令/AI 生成/绑定草稿/保存预览/保存/刷新效果链 |
| 快照语义 | 最新胜出（LatestWins） |

## 3. 文件结构

`mods/capabilities/live_skill_workbench/LiveSkillWorkbenchMod/`（能力 mod 本体：DataPlane、运行时、Web 资产）；引擎侧核心在 `src/Core/Gameplay/GAS/LiveSkillWorkbench/`。

## 4. 运行时加载效果

能力 mod 启动时绑定会话/流水线/效果链/AI/保存五个服务并安装 DataPlane；未挂保存目标 mod 前保存命令拒绝；会话与运行注册表隔离（暂存不生效直到提交）。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 暂存非法编辑 | 拒绝并带 LSW 诊断码（LSW0001-0021 段），暂存区不变 |
| 无安全帧提交下次施放级 | 拒绝（要求安全帧） |
| 提交中途失败 | 逆序回滚已提交项；回滚失败显式报最高级错误 |
| 保存目标未配置 | 保存命令拒绝并说明 |

## 6. 实例

- 能力 mod：`mods/capabilities/live_skill_workbench/LiveSkillWorkbenchMod/`
- 验收样例：`mods/showcases/capability_standard/CapabilityStandardLiveSkillWorkbenchShowcaseMod/`

**相关文档**：[ed-01 PRD](../prd/ed-01-workbench-base.md) · [cfg-01 配置说明](cfg-01-mod-manifest.md) · [ed-02 配置说明](ed-02-hot-apply.md)
