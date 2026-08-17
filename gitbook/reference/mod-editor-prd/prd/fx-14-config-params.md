# fx-14 · 参数化

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/fx-14-config-params.md)；编辑器需求见 [UXD](../uxd/fx-14-config-params.md)；引擎实现见 [runtime spec](../spec-runtime/fx-14-config-params.md)；editor spec 见 [editor spec](../spec-editor/fx-14-config-params.md)；现状见 [reference](../reference/fx-14-config-params.md)。

## 1. 定位

configParams 是效果模板的具名参数槽：模板写默认值，施放链按同名键覆盖——一份模板支撑一族数值与引用变体。

## 2. 产品承诺

- **七种值类型**：Float/Int 两种数值，与效果模板、属性、兑换操作、实体模板、生命周期取值来源五种注册表引用。
- **保留键**：`_ep.` 前缀是引擎保留键，语义由各效果域定义；其余键由 mod 自由命名。
- **引用加载期锁定**：引用类型的名字在加载期解析为注册 id，写错名字启动失败并指明键。
- **caller 覆盖**：施放侧 CallerParams 同键覆盖模板值（值与类型一起改写），异键追加；合并结果在效果实例存续期内固定。
- 参数条数上限见事实页：模板侧超限启动失败；caller 侧追加超限必须可观测。

## 3. 运行行为

实体化效果在创建时预合并参数并存为组件；Instant 内联路径每次执行现算合并；引用键运行期只认 id，不再查名字。

## 4. 异常承诺

未知类型、未知引用名、保留键类型不符、模板侧超容量——启动失败并指明键与位置。

**相关文档**：[配置说明](../config/fx-14-config-params.md) · fx-02、ab-03（CallerParams 参数池）
