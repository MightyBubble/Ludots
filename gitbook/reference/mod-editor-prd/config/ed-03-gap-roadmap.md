# ed-03 配置说明 · 编辑器缺口与路线图

> 配置写法与行为。第一性需求见 [ed-03 PRD](../prd/ed-03-gap-roadmap.md)；编辑器需求见 [UXD](../uxd/ed-03-gap-roadmap.md)；现状见 [reference](../reference/ed-03-gap-roadmap.md)。

## 1. 示例配置

本篇记录的是缺口，不引入任何新配置。与解锁缺口相关的现有配置只有一项——挂载保存目标 mod（工作台保存命令的目标，见 ed-01）：

```text
保存目标：挂载 CapabilityStandardLiveSkillWorkbenchShowcaseMod 后生效
```

## 2. 作者可配什么与在哪配

| 缺口 | 作者能做什么 | 解锁条件 |
|---|---|---|
| 目录树空态 | 无（文档投影源缺生产实现） | 引擎侧补生产投影源（治理项 R5） |
| 撤销/重做 | 无配置；暂存条用"丢弃全部"替代 | 会话撤销栈接入 |
| 图编辑器 | 改图走手写图文档（gr-02 格式）经热替换生效 | 图编辑器立项（gr 卷配套） |
| 冷编辑流 | 离线改文件+重启游戏（当前唯一完整链路） | 冷编辑流立项 |

规则：解锁缺口一律不通过新增 mod 配置实现——缺口是编辑器/引擎侧工程项，配置面保持零增长。

## 3. 文件结构

无新增文件。缺口清单的真源在运行时代码与前端资产（见 reference）。

## 4. 运行时加载效果

运行时按绑定状态生成不可用动作清单并随快照发布；文档投影源为可选服务注入，未注入时目录树空态；对不可用动作的调用返回 LSWUI 诊断码。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 调用不可用动作 | 返回对应 LSWUI0001-0009 码与解锁条件 |
| 文档投影源未注入 | UI 空态说明，不报错不崩 |
| 保存根未配置 | 保存命令拒绝（同 ed-01） |

## 6. 实例

- 不可用清单：`mods/capabilities/live_skill_workbench/LiveSkillWorkbenchMod/Runtime/LiveSkillWorkbenchRuntime.cs`（BuildUnavailableActionsUnlocked）
- 前端占位区：`mods/capabilities/live_skill_workbench/LiveSkillWorkbenchMod/Assets/live-skill-workbench-app/`

**相关文档**：[ed-03 PRD](../prd/ed-03-gap-roadmap.md) · [ed-01 配置说明](ed-01-workbench-base.md) · [UXD](../uxd/ed-03-gap-roadmap.md)
