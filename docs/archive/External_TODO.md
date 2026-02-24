# External TODO（本轮不做）

本文件用于明确“需要外部库/大型工程投入”的事项，避免在 GAS 缺口修复期发生范围膨胀。这里的条目不进入当前 Backlog 的 P0/P1 执行序列，除非另行立项。

## 1) 代码生成 / 编译期工具链

- Roslyn Source Generator：
  - 用途：CoreAttribute 访问器字段化、强类型 Token（TagId/AttributeId/EffectTemplateId/GraphId）生成、magic string 消除
  - 暂缓原因：需要引入编译期工程与 CI 约束，且对现有工程结构影响较大
  - 触发条件：Backlog 中的“ID 化/禁字符串热路径”开始出现大量手工维护成本时

## 2) 可视化 Graph 编辑器与导出链路

- Unity Editor/自研图编辑器：
  - 用途：内容侧编辑 Graph、导出 GraphConfig/二进制产物、做节点白名单与预算可视化
  - 暂缓原因：前置依赖较重（Editor UI、资源导入导出、版本兼容策略），容易把“运行时缺口”拖入“内容工具”泥潭
  - 触发条件：Graph 产线闭环（Backlog GAS-BL-005）稳定后，再评估工具投入

## 3) 全量静态审计（分配/结构变更/确定性）

- IL Rewriter / 运行时探针 / Roslyn Analyzer：
  - 用途：自动检测热路径分配、Query 中闭包、并行 Job 内结构变更、禁止 API 调用等
  - 暂缓原因：属于“平台级质量门禁”，需要统一全项目治理，而非 GAS 子系统局部优化
  - 触发条件：预算/熔断与 fail-fast（Backlog GAS-BL-007）稳定后，开始建设全局质量门禁

## 4) 回放/锁步一致性测试基建

- Deterministic Replay Harness（含快照/差分/随机流审计）：
  - 用途：跨平台/跨机器验证同输入流下的状态一致性，捕捉排序/RNG/浮点差异
  - 暂缓原因：需要统一的序列化/快照协议与长期维护成本
  - 触发条件：StableId/StableKey 全链路被广泛依赖、多人联机/回放进入里程碑

## 5) 复杂数据协议与 Schema 治理

- JSON Schema / 配置校验框架 / 版本迁移器：
  - 用途：对 effects.json / tagrules.json / graphs.json 做 schema 校验、版本升级、字段废弃策略
  - 暂缓原因：当前优先级应聚焦“单一真源 + fail-fast + 预算口径”，先把最小链路跑通
  - 触发条件：配置规模上升、Mod 生态扩展后再统一治理
