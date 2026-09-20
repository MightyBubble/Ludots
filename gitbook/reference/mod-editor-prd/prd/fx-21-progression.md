# fx-21 · 进度完成

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/fx-21-progression.md)；编辑器需求见 [UXD](../uxd/fx-21-progression.md)；引擎实现见 [runtime spec](../spec-runtime/fx-21-progression.md)；editor spec 见 [editor spec](../spec-editor/fx-21-progression.md)；现状见 [reference](../reference/fx-21-progression.md)。

## 1. 定位

CompleteProgression 效果完成一次进度：直接完成、设到指定等级、或推进增量——科技解锁、任务推进、市政升级的效果侧入口。

## 2. 产品承诺

- **专属组合**：progression 块只属于 CompleteProgression preset，必须 Instant。
- **注册合同**：progression.id 必须是进度注册表中已登记的名字。
- **作用域三态**：`self`、`explicit`、命名作用域（须在进度作用域表声明）。
- **变更三选一**：level 与 delta 互斥且都必须为正，都不写即"直接完成"。
- 进度属外部进度原子域：独占效果计划；作用域宿主上必须已就位进度状态缓冲，否则执行失败。

## 3. 运行行为

作用域宿主按三态解析（施法者/受术者/显式宿主）；进度求值器按 id 与变更量应用等级变化；等级回退类变更一律拒绝。

## 4. 异常承诺

块与 preset 不匹配、id 未注册、作用域未声明、level 与 delta 同写或非正——启动失败并指明字段；运行期作用域宿主不可解析、状态缓冲缺失——抛错。

**相关文档**：[配置说明](../config/fx-21-progression.md) · 见 misc-01（进度域三张表）
