# 数据配置类与通用合并策略最佳实践

本篇给出一个统一的配置设计与合并框架，目标是避免“每种配置一个独立处理器”。  
无论是 `game.json`、地图配置、GAS 配置，还是未来 Mod 自定义数据类，都应优先复用同一套合并语义。

相关代码入口：

*   合并管线入口：`src/Core/Config/ConfigPipeline.cs`
*   合并策略枚举：`src/Core/Config/ConfigMergePolicy.cs`
*   合并器实现：`src/Core/Config/ConfigMerger.cs`
*   配置目录条目：`src/Core/Config/ConfigCatalogEntry.cs`

## 1 先分型，再选策略

先判断你的配置属于哪一类，再选对应 merge 策略，而不是“按文件名写死逻辑”。

### 1.1 单例对象（Singleton）

示例：全局运行参数、单份 UI 配置。

推荐：

*   `DeepObject`：对象递归合并，标量后者覆盖前者。
*   `Replace`：整份替换（只在你明确想“一刀切”时使用）。

### 1.2 键控表（Array/Table By Id）

示例：技能模板、效果模板、单位模板。

推荐：

*   `ArrayById`，并指定稳定主键（默认 `Id`，也可在 `ConfigCatalogEntry` 指定其他字段）。
*   删除用 `__delete=true`（兼容 `Disabled=true`）。

### 1.3 追加表（Append List）

示例：日志规则、额外提示、简单扩展列表。

推荐：

*   `ArrayAppend`，仅用于“天然可重复、顺序敏感”的列表。
*   若需要去重或覆盖，不要用 Append，改用 `ArrayById`。

### 1.4 整体替换数组

示例：完整排序表、完整白名单。

推荐：

*   `ArrayReplace`，并在文档明确“后者覆盖前者，不做拼接”。

## 2 配置类字段设计规范

### 2.1 主键字段必须稳定

*   任何需要增量覆盖的表，必须有稳定主键（如 `Id`）。
*   主键禁止使用索引/位置语义（例如“第 3 条”）。

### 2.2 字段类型只做三件事

*   标量：`Replace`
*   对象/字典：`DeepObject`
*   数组：显式声明策略（`ArrayReplace/ArrayAppend/ArrayById`）

不要让数组走“隐式默认行为”。

### 2.3 删除语义必须标准化

*   表项删除统一使用 `__delete=true`（可兼容 `Disabled=true`）。
*   禁止通过“写空对象”或“写 null 猜测删除”。

### 2.4 多维表按“外层键控 + 内层策略”处理

二维/多维配置不要自定义独立合并器，拆成：

1. 外层 `ArrayById`
2. 内层对象 `DeepObject`
3. 内层数组按字段声明 `Append` 或 `Replace`

## 3 Mod 自定义配置类接入流程

1. 选定相对路径（例如 `Gameplay/MyFeature/rules.json`）。
2. 在配置目录中声明 `ConfigCatalogEntry`，明确 `MergePolicy` 与 `IdField`。
3. 使用 `ConfigPipeline.MergeFromCatalog(entry[, report])` 获取合并结果。
4. 反序列化为你的数据类并在系统初始化阶段注册。
5. 需要追踪覆盖关系时，强制使用带 `ConfigConflictReport` 的路径。

## 4 推荐与反模式

推荐：

*   用 `ArrayById` 处理“可被 Mod 覆盖”的模板类数据。
*   用 `DeepObject` 处理单例配置。
*   在文档中写清楚“该文件采用的 MergePolicy”。

反模式：

*   每个功能模块写一套自己的 JSON 合并器。
*   用数组位置表示身份。
*   依赖“最后加载刚好覆盖”的隐式行为而不声明策略。

## 5 设计检查清单

在新增配置类前，先回答这 6 个问题：

1. 这是单例对象还是键控表？
2. 主键字段是什么？是否稳定？
3. 数组字段到底要 Replace、Append，还是 ById？
4. 删除怎么表达？是否统一 `__delete`？
5. 冲突是否要进入 `ConfigConflictReport`？
6. 对应路径是否已在文档与目录中可追踪？

如果以上任一问题答不清，先不要落代码，先补策略声明。
