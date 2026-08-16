# gr-02 · 六种 Kind

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/gr-03-kinds.md)；编辑器需求见 [UXD](../uxd/gr-03-kinds.md)；引擎实现见 [runtime spec](../spec-runtime/gr-03-kinds.md)；编辑器实现见 [editor spec](../spec-editor/gr-03-kinds.md)；现状见 [reference](../reference/gr-03-kinds.md)。

## 1. 定位

kind 回答一张图"是什么、怎么收尾、能写哪些节点"：Effect、Query、Score、Validation、Derived、Script 六值封闭，各有返回槽约定与节点白名单。

## 2. 产品承诺

- **一 kind 一返回约定**：Script 经返回节点把整数写回调用方；Score 写浮点分值；Validation 写布尔判定；Effect 只做事不返回；Query 产出目标列表与输出物化；Derived 直写自身属性。
- **白名单编译期强制**：越权节点（如 Script 专属控制流进了 Effect 图）在装载时拒绝，不留运行期惊喜。
- **预设实体寄存器受保护**：E0/E1/E2 由宿主绑定、编译期保留，作者不可侵占。
- **返回槽受保护**：各 kind 的宿主返回寄存器同样编译期保留，图内写用不进返回槽。

## 3. 运行行为

kind 在文档头声明、装载期定死：注册后不可改；挂接点只接受声明的 kind（gr-07）。

## 4. 异常承诺

越权节点、图未按约定收尾、kind 与挂接点不符——装载或挂接失败，指明图与节点。

**相关文档**：[配置说明](../config/gr-03-kinds.md) · [gr-00](gr-01-model.md) · [gr-07](gr-08-mount-points.md)
