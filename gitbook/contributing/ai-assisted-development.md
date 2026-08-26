# AI 辅助开发规范

本页是 Ludots 中所有 AI Agent 的正式工作规则。写第一行代码前，必须先读完本页的“任务执行决策规范”。

## 1 核心流程

```text
搜索已有能力 → 阅读相关文档和源码 → 列出复用/新增清单 → 编码 → 验证 API 引用
```

前三步不得跳过。

**GAS / 实体生命周期 / effect preset / graph op 类任务**：编码前还必须执行共享 skill `ludots-gas-composition-gate`（见 `skills/governance/ludots-gas-composition-gate/`），填写自审清单并产出 `artifacts/gas-composition-gate.md`。核心判断标准：新变体应新增 graph 节点或 effect 步骤，而不是 profile enum 或 preset 开关。

## 2 防幻觉条款

- 禁止凭空发明类、方法、字段、Registry 能力和 NuGet API。
- 每引用一个非 BCL 的类型或方法，必须先搜索确认存在。
- 完成编码后，必须对新增的构造、方法调用和类型引用做一次存在性自检。

## 3 防重复造轮子条款

- 不得未经搜索就新建 Registry、事件系统、配置加载器或平行组件体系。
- 先看架构入口，再设计挂靠点。
- 开工前必须能列出复用清单；列不出来说明发现阶段没做完。

## 4 任务执行决策规范

### 4.1 判断一：这是不是我该直接做的事

- 如果只是沿现有管线做增量，可以继续。
- 如果需要新建管线、修改 Core 接口或重做基础设施，这是基建任务，应先说明方案再继续。
- 如果需求描述不足以判断，不得基于猜测直接动手。

### 4.2 判断二：我需要复用什么基建

开始编码前，必须显式列出复用项，例如：

- Registry：用于注册和查找
- Pipeline：数据从哪里流向哪里
- System：在哪个 phase 扩展
- Mod：是否应在已有 Mod 基础上增量实现

### 4.3 判断三：基建够不够用

- 缺一个现有 Registry 的方法或字段，先补已有基建。
- 缺一整条管线，先停下来说明缺口，不在 feature 中临时造一条。
- 接口不匹配时，先说明重构点，不绕过正式链路。

### 4.4 Mod 提取规则

- 只服务当前 Mod 的逻辑，放在当前 Mod。
- 两个以上 Mod 可能复用的逻辑，提取到 Core 或可复用基础设施。
- 完整独立功能，提取为独立 Mod。

### 4.5 GAS 组合自审（与 skill 绑定）

适用：新增/修改 `BuiltinHandler`、`EffectPresetType`、实体 spawn/morph/lifecycle、graph op、或 `*_profiles.json` 类 schema。

1. 加载 skill `ludots-gas-composition-gate` 及 `gitbook/architecture/entity-lifecycle-atomic-ops.md`。
2. 回答判断标准：新变体是 **op 组合** 还是 **新 enum/开关**；后者禁止直接开工。
3. 填写 `references/self-review-checklist.md` 模板，写入 `artifacts/gas-composition-gate.md`。
4. 实现 PR 须链接该自审产物或等效填写内容。

### 4.6 面向人的中文（与 skill 绑定）

适用：写或改 `gitbook/` 正式文档、showcase 设计说明、门户说明、PR/Issue 里给人看的段落。

1. 加载共享 skill `shuorenhua`（`skills/governance/shuorenhua/`）。
2. 按该 skill 的固定顺序处理：判场景 → 划 protected spans → Tier → 档位 → 改写 → 回读。
3. 默认场景是 `docs`；README / release note / issue 回复等走对应 Scene Pack。
4. 保事实、术语、路径、命令与责任主体；禁止为了「顺口」改掉合同含义。
5. 给用户的最终说明也按同一标准：能用业务话讲清的，不要堆实现腔。

## 5 现有能力速查

优先复用这些正式基础设施：

- Registry：`SystemFactoryRegistry`、`AttributeRegistry`、`TagRegistry`、`AttributeSinkRegistry`、`AbilityDefinitionRegistry` 等
- 核心管线：ConfigPipeline、GAS Effect Pipeline、Presentation Pipeline、Trigger Pipeline、Mod Loading、Startup、UI Runtime
- SystemGroup：`SchemaUpdate → InputCollection → PostMovement → AbilityActivation → EffectProcessing → AttributeCalculation → DeferredTriggerCollection → Continuation → Cleanup → EventDispatch → ClearPresentationFlags`

## 6 深度材料

- 仓库深度版：`docs/conventions/02_ai_assisted_development.md`
- 架构入口：`docs/architecture/README.md`
