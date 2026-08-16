# cfg-08 · mod 代码扩展面

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/cfg-08-mod-extensions.md)；编辑器需求见 [UXD](../uxd/cfg-08-mod-extensions.md)；引擎实现见 [runtime spec](../spec-runtime/cfg-08-mod-extensions.md)；编辑器实现见 [editor spec](../spec-editor/cfg-08-mod-extensions.md)；现状见 [reference](../reference/cfg-08-mod-extensions.md)。

## 1. 定位

mod 用代码给引擎注册新积木的通道：新效果处理器、新图节点、新表现命令与行为。"组合优先于改 Core"的正规实现。

## 2. 产品承诺

- **四个注册面**：效果内建处理器、图节点、表现器命令、表现器行为——经入口上下文的扩展门面注册。
- **只在加载窗口**：注册仅发生在入口加载期间；扩展枢纽冻结后再注册即拒绝启动。
- **语义键单主**：一个键一个主人，重复或撞名即拒绝。
- **注册先于配置编译**：代码注册的键，配置编译期即可引用——新积木不需要新 schema。
- **全部 fail-fast**：重复键、配置引用未注册键、缺登记、缺分片——启动失败并指明环节。

## 3. 运行行为

装配期注册 → 枢纽冻结 → 配置编译解析键 → 运行期执行注册的代码。

## 4. 异常承诺

冻结后注册、重复键、撞名、配置引用未注册键——一律启动失败。

**相关文档**：[配置说明](../config/cfg-08-mod-extensions.md) · [UXD](../uxd/cfg-08-mod-extensions.md) · [cfg-01](cfg-01-mod-manifest.md)
