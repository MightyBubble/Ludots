# gr-02 · 图文档格式

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/gr-02-document.md)；编辑器需求见 [UXD](../uxd/gr-02-document.md)；引擎实现见 [runtime spec](../spec-runtime/gr-02-document.md)；editor spec 见 [editor spec](../spec-editor/gr-02-document.md)；现状见 [reference](../reference/gr-02-document.md)。

## 1. 定位

图的书写与交换格式：一份 JSON 文档 = 一张控制流图 + 一张值流图。节点是常量、操作与符号引用的载体，边是唯一顺序事实。

## 2. 产品承诺

- **顺序全部显式**：节点之间怎么走由边说了算；节点内写 next 顺序字段被硬拒。
- **双图键必须齐**：controlEdges 与 valueEdges 两键都必须出现，空图也要写空数组——缺键是格式错误不是省略。
- **kind 必填**：没有默认 kind；文档必须显式声明自己的种类并接受相应创作约束（gr-03）。
- **端口封闭**：端口名是常量集合，不开放自造端口；分支端口由边携带。
- **id 宽容**：节点 id 可省略，装载器补全；引用大小写不敏感。

## 3. 运行行为

文档只经一个创作门进入编译：门内完成 kind 检查、next 拒绝、边键强制与 id 补全，之后交给编译器（gr-04）。

## 4. 异常承诺

缺 kind、带 next、缺任一边键、未知端口或未知节点字段——装载失败并指明文档与位置，绝不静默降级。

**相关文档**：[配置说明](../config/gr-02-document.md) · [gr-01](gr-01-model.md) · [gr-04](gr-04-compilation.md)
