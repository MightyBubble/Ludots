# gr-06 · 函数库 FuncLib

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/gr-06-funclib.md)；编辑器需求见 [UXD](../uxd/gr-06-funclib.md)；引擎实现见 [runtime spec](../spec-runtime/gr-06-funclib.md)；editor spec 见 [editor spec](../spec-editor/gr-06-funclib.md)；现状见 [reference](../reference/gr-06-funclib.md)。

## 1. 定位

FuncLib 给图起函数名：把已注册的 Script 图收进可跨图调用的函数目录，供 InvokeFunc 按名调用。

## 2. 产品承诺

- **函数必须纯**：入库图从任何路径都不可达挂起——可达即拒，含跨图调用环。
- **名字唯一**：函数名是跨图调用的解析键，且不得与动作库撞名。
- **先图后库**：函数引用的图必须先注册、kind 一致；库装载后统一回写解析再终检。
- **一次声明到处调用**：调用点写函数名，装载期换成图 id 并清标记位，运行期零字符串。

## 3. 运行行为

装载顺序固定：graphs 注册 → FuncLib 装载与纯度校验 → 调用点统一解析与终检 → ActionLib（gr-07）。

## 4. 异常承诺

kind 不是 Script、引用未注册图或 kind 不一致、可达挂起、调用环、与动作库撞名——装载失败并指明函数名。

**相关文档**：[配置说明](../config/gr-06-funclib.md) · [gr-04](gr-04-compilation.md) · [gr-07](gr-07-actionlib.md)
