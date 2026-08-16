# gr-op-04 · 节点：属性与配置

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/gr-op-04-attributes.md)；编辑器需求见 [UXD](../uxd/gr-op-04-attributes.md)；引擎实现见 [runtime spec](../spec-runtime/gr-op-04-attributes.md)；编辑器实现见 [editor spec](../spec-editor/gr-op-04-attributes.md)；现状见 [reference](../reference/gr-op-04-attributes.md)。

## 1. 定位

图读数值状态的入口与唯一的直写口：读任意实体属性、读自身属性、直写自身属性、读三条配置键（Float/Int/EffectId）。

## 2. 产品承诺

- **读属性按符号**：LoadAttribute 输入实体加属性名，读出 Float 当前值；属性名在编译期经属性注册表解析。
- **自身快捷读**：LoadSelfAttribute 省去 source 引脚，直接读图宿主自己的属性。
- **唯一的图内直写口**：WriteSelfAttribute 把 value 直写自身属性的 Current——绕过修改器聚合管线，是"改基础数值"效果的对偶面，也是 Derived 图回写自身的通道。
- **配置即数值源**：LoadConfig 三件按配置键读常量值；改配置不改图。

## 3. 运行行为

LoadAttribute 读实体属性缓冲的 Current；WriteSelfAttribute 在事务内直写 SetCurrent（不建修改器、不触发重聚合）；LoadConfig 在图加载时绑定配置键，执行期读注册表当前值。

## 4. 异常承诺

引用未注册属性名或配置键——编译失败并指明节点与名字。监听宿主的图使用 LoadConfig——编译拒绝（无 owner 模板上下文）。

**相关文档**：[配置说明](../config/gr-op-04-attributes.md) · [attr-01](attr-01-definition.md) · [gr-op-10](gr-op-10-effect-actions.md) · [节点画廊 wiki](../../graph-node-op-wiki/README.md)
