# Presenter children 编译为 CreatePresenter 命令计划 (#1098)

## Scenario Card
- Player goal: 声明式 presenter children 不再在 CreateHierarchy 内隐藏递归创建，而是编译为显式 CreatePresenter plan nodes，由统一命令路径执行且每个 node 可检索 trace。
- Scope: `src/Core/Presentation/Presenters/PresenterCreatePlan.cs`（新增 compiler）、`PresenterDefinitionRegistry`（plan 缓存 + epoch 失效）、`PresenterDefinitionConfigLoader`（装载期编译）、`PresenterEntityRuntime`（计划执行 + trace 环形缓冲）。
- Build: local PresentationTests / Ludots.Client.Raylib / Ludots.Launcher.Backend，分支 `codex/issue-1098-children-create-plan`（基于 c497ea5e29）。

## Timeline
- [T+000] `compile_plan` -> `PresenterCreatePlanCompiler.Compile` 按声明顺序把 children（含 instanceChildren 物化）展平为 parent-before-child 的 plan nodes，固化 parent edge、scope、param/transform override。
- [T+001] `registry_cache` -> `PresenterDefinitionRegistry.GetOrCreateCreatePlan` 缓存编译结果，注册/移除时按 epoch 失效；config loader 装载后 `CompileAllCreatePlans` 预热。
- [T+002] `single_create_path` -> `CreateHierarchy` 删除 `CreateChildrenRecursive`，改为执行 `ExecuteCreatePresenterPlan`（每 node 走 `CreateFromPlanNode`）；批量路径 `CreateChildrenBatch` 同样按 plan nodes 驱动。runtime 不存在第二条直接创建路径。
- [T+003] `failure_semantics` -> parent 缺失（`PlanParentMissingError` 带 childPath/planNode）、override 类型错误（编译期 `ParamOverrideTypeError` 带 childPath）、容量不足（`PlanNodeFailedError` 包装 + `exceeded child capacity`）均为结构化错误；`CreateHierarchy` 失败时回收已建实体，无半棵树残留。
- [T+004] `trace` -> 根（nodeIndex=-1）与每个 plan node 写入 `PresenterCreateTraceEntry` 环形缓冲（容量 8192），`FindCreateTraces(rootStableId)` 可检索。
- [T+005] `instance_parity` -> #992/#993 语义保持：compiler 把 childrenMode=Instance 的 instanceChildren 物化进 plan（空 payload 截断子树、嵌套 instance 继续下探），`AttachChildInstanceOverride` 保留 PresenterInstanceChildren / PresenterInstanceBehaviors / mask OR / bootstrap 标记；definition 子树不被实例改写。

## Outcome
- success: yes
- 每个 child 均有 plan node 与 parent edge；声明顺序与 parent-before-child 由编译期保证（含循环引用拒绝）。
- children 行为逐字段对照测试先于 runtime 改动锁定（scope 继承/scopeTag 覆盖、param override、transform override、声明顺序、instance 子树替换仅作用于该实例）。
- 三类失败结构化错误 + 无残留 child；既有 tree lifecycle / config loader 定向测试全绿。

## Verification Matrix
- `PresenterCreatePlanTests`：14/14 通过（对照锁定 2、compiler 6、registry 缓存 1、trace 2、失败语义 3）。
- `PresenterTreeLifecycleTests` + `PresenterDefinitionConfigLoaderTests` 定向：168/168 通过（含 instance children 3 项既有行为测试）。
- 全量 PresentationTests：813 总计，793 通过，20 失败；19 个与 base c497ea5e29 完全一致（crowd physics arena showcase、genre info showcase、Mesh/LOD/Text catalog loader、scatter/minimap benchmark、vfx map load），`Showcase_BootsArenaWithKinematicSquadsDrivenByBridge` 在 base 与本分支隔离/类内运行均通过、仅全量并发负载下偶发（kinematic feed 计数时序断言），与本改动无关。
- 消费方构建：Ludots.Client.Raylib 0 错误；Ludots.Launcher.Backend 0 错误。

## Summary Stats
- 新增：`src/Core/Presentation/Presenters/PresenterCreatePlan.cs`（compiler + plan/trace 契约）、`src/Tests/PresentationTests/Presenter/PresenterCreatePlanTests.cs`。
- 修改：`PresenterDefinitionRegistry`（+36）、`PresenterEntityRuntime`（+322/-84，递归创建删除）、`PresenterDefinitionConfigLoader`（+2）。
- trace 证据：`trace.jsonl`
