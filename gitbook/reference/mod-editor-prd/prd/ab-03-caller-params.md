# ab-03 · CallerParams 参数池

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/ab-03-caller-params.md)；编辑器需求见 [UXD](../uxd/ab-03-caller-params.md)；引擎实现见 [runtime spec](../spec-runtime/ab-03-caller-params.md)；editor spec 见 [editor spec](../spec-editor/ab-03-caller-params.md)；现状见 [reference](../reference/ab-03-caller-params.md)。

## 1. 定位

CallerParams 是技能向效果传参的通道：同一条时间轴让多个效果条目复用一组或几组数值参数，效果模板读同一键名拿到不同值。参数化效果的"调用方实参"。

## 2. 产品承诺

- **池式声明**：技能声明至多四组参数集，时间轴条目按索引引用一组；不引用即无参数。
- **同键覆盖**：调用方参数与效果模板自带参数同键时，调用方胜。
- **空间参数自动注入**：时间轴带目标位置时，目标点与原点坐标四个键自动追加，效果免声明。
- **纯数值**：池只存 float 键值对；复杂参数仍属效果模板与图。
- **失败可见**：参数追加失败必须让技能失败，不允许静默丢参后效果算错。

## 3. 运行行为

编译期池编入技能定义（键注册、值定档）；触发效果条目时取出所引组、叠加空间参数、随效果请求下发；合并发生在效果侧读取参数时。

## 4. 异常承诺

单组参数超上限、池组数超上限——启动失败并指明条目路径；运行期空间参数追加失败——技能失败。

**相关文档**：[配置说明](../config/ab-03-caller-params.md) · [ab-02](ab-02-exec-timeline.md) · [fx-18](fx-14-config-params.md)
