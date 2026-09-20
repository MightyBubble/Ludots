# cfg-08 UXD · 代码扩展面的编辑器需求

> cfg-08 的编辑器需求。第一性需求见 [cfg-08 PRD](../prd/cfg-08-mod-extensions.md)；配置写法见 [cfg-08 配置说明](../config/cfg-08-mod-extensions.md)；编辑器实现见 [editor spec](../spec-editor/cfg-08-mod-extensions.md)。

## 1. 界面定位

让"代码积木"在编辑器里可见、可选、可校验——作者不必读代码就知道有哪些扩展键可用。

## 2. 界面功能

- **扩展键浏览**：按四个注册面分组列出本 mod 与依赖 mod 已注册的键（含来源 mod、元数据摘要）。
- **图节点面板动态化**：节点库按已注册图 op 动态生成，而非写死的内置清单。
- **处理器选择器**：预设编辑里 builtin id 从已注册键下拉选择，杜绝拼错。
- **引用检查前移**：配置里的扩展键引用编辑期即校验存在性。

## 3. 数据存储

扩展键清单为运行时投影，不落盘缓存。

## 4. 易用性设计

- 键的元数据翻译成作者语言：处理器显示"纯操作，可用于计算相位"等。
- 未注册键的引用报错附"可用键列表"与最近似建议。

**相关文档**：[cfg-08 PRD](../prd/cfg-08-mod-extensions.md) · [cfg-08 配置说明](../config/cfg-08-mod-extensions.md) · [editor spec](../spec-editor/cfg-08-mod-extensions.md)
