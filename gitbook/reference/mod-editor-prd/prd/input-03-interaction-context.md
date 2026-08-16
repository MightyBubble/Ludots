# input-03 · 交互上下文档案

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/input-03-interaction-context.md)；编辑器需求见 [UXD](../uxd/input-03-interaction-context.md)；引擎实现见 [runtime spec](../spec-runtime/input-03-interaction-context.md)；编辑器实现见 [editor spec](../spec-editor/input-03-interaction-context.md)；现状见 [reference](../reference/input-03-interaction-context.md)。

## 1. 定位

交互上下文档案是能力的环境声明：能力执行期间激活哪个实体集合、哪个过滤档案、哪个输入上下文与命令意图——执行开始帧上栈，结束即回收。

## 2. 产品承诺

- **声明式激活**：能力只声明档案，压栈与回收由系统自动完成，作者不写生命周期代码。
- **五个拼装位**：活动集合键、活动实体视图键、过滤档案（可空=直通）、输入上下文、命令意图——全部可选，按需拼装。
- **栈式优先**：栈顶帧的命令意图优先于控制方案默认（对接 input-01 解析链）。
- **空档案表合法**：引擎默认无任何档案；不声明档案的能力行为不变。

## 3. 运行行为

声明了档案的能力在 exec 开始时压帧上交互上下文栈；期间命令意图仲裁以栈顶为准；exec 结束按上下文实体回收帧，栈恢复原状。

## 4. 异常承诺

声明了不存在档案的能力在执行开始即失败并指明能力与档案名；档案声明的空引用（空串）在能力加载期失败。

**相关文档**：[配置说明](../config/input-03-interaction-context.md) · [input-01](input-01-command-intent.md) · [input-05](input-05-filters-and-schemes.md)
