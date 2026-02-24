# GasTests 测试编写风格规范（唯一真源）

本文档规定 `src/Tests/GasTests` 下的测试编写风格与约束，目标是：用例可读、断言可追踪、运行确定性、对性能/零 GC 约束不引入噪音。

## 1. 命名规则

- 测试类：`<Subsystem>Tests`，示例：`DeferredTriggerCollectionTests`。
- 测试方法：`<Subject>_<Scenario>_<Expected>`，示例：`DeferredTriggerProcessSystem_AttributeChanged_PublishesMappedEventTag`。
- 同一主题的对称用例：使用一致的 `<Scenario>` 前缀，便于 filter 执行与矩阵追踪。

## 2. 结构模板（AAA）

所有测试必须按 AAA（Arrange/Act/Assert）三段组织：

- Arrange：构造 world、entity、组件、registry 映射，准备输入数据。
- Act：调用系统或 API（通常 1–2 行），避免在 Act 段写多余逻辑。
- Assert：只做断言与最小诊断；断言失败时能定位“哪个语义点被破坏”。

## 3. 断言约定

- 统一使用 NUnit：`Assert.That(actual, Is.EqualTo(expected))`。
- 避免混用多个断言风格或断言库，避免引入不同失败信息格式。
- 断言失败信息必须可自解释：必要时使用 `Assert.That(..., "message")` 提供一句话上下文。

## 4. 资源与静态状态清理（防止测试互相污染）

### 4.1 World 生命周期

- 使用 `using var world = World.Create();` 或在 `[TearDown]` 中 `Dispose()`。
- 禁止跨测试共享 `World` 实例。

### 4.2 静态 Registry 清理

以下类型存在静态状态，相关测试必须在 `finally` 或 `[TearDown]` 中显式清理：

- `TagOps.ClearRuleRegistry()`（规则表是静态实例）。
- 若测试依赖 Attribute/Tag 注册表的“干净起点”，应提供统一 helper（见 6.2）做隔离或重置策略。

## 5. 确定性与零 GC 约束

### 5.1 禁止项（测试代码）

- 禁止在测试中使用 `Console.WriteLine` 作为常规输出。
  - 需要诊断时仅允许使用 `TestContext.WriteLine`，且必须在断言失败分支或 catch/finally 中输出最小信息。
- 禁止在测试中引入非确定来源：真实时间、随机数、线程竞态。
- 禁止在 GAS 热路径测试中使用 LINQ。

### 5.2 EventBus 访问约束（热路径一致性）

- `GameplayEventBus.Events` 必须使用索引访问（`for` + `events[i]`），不得 `foreach`。
  - 目的：避免 `yield return` 枚举器分配导致 GC 噪音，污染基准与回归判断。

## 6. 复用与样板代码治理

### 6.1 不重复粘贴样板

当以下模式重复出现时，应提取为统一 helper（一个 helper 一个职责）：

- 创建 world + 创建实体 + 挂组件。
- 构造 DeferredTriggerQueue + Collection/ProcessSystem + EventBus 并驱动一次帧。
- 构造 TagRuleSet/AttributeEventTagRegistry 映射的常用模板。

### 6.2 推荐的 helper 形态（约束）

- Helper 只能放在 GasTests 项目内（`src/Tests/GasTests`），不得进入 Core Runtime。
- Helper 必须是小而专的静态方法或轻量 struct，不得引入复杂框架或反射。
- Helper 里不得做全局副作用（例如静态单例缓存 world），避免跨测试污染。

## 7. 与文档用例（MUD）的一致性

- 用例来源：`docs/08_能力系统/05_用例/00_MUD_验收需求.md`。
- 每个自动化测试方法应在文档矩阵中可追踪（见矩阵文档），确保“文档语义点”与“代码断言点”一一对应。

