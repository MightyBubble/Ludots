# infra-04 · 界面档案

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/infra-04-ui-profiles.md)；编辑器需求见 [UXD](../uxd/infra-04-ui-profiles.md)；引擎实现见 [runtime spec](../spec-runtime/infra-04-ui-profiles.md)；editor spec 见 [editor spec](../spec-editor/infra-04-ui-profiles.md)；现状见 [reference](../reference/infra-04-ui-profiles.md)。

## 1. 定位

界面域三张档案表决定 HUD 的形状：技能聚合档案声明实体的技能按什么维度归组显示，命令甲板与生产总览档案声明对应面板的布局参数。底座给默认值，皮肤与玩法 mod 覆盖它们。

## 2. 产品承诺

- **聚合可编程但封闭**：groupBy 用内建表达式（按模板、按技能 id）声明归组维度，安装期编译——不开放任意表达式，写未知表达式即失败。
- **面板档案可空壳**：命令甲板与生产总览的根表是空占位（`{"profiles":[]}`），内容一律由 mod 下沉——底座不预设布局，皮肤说了算。
- **三表同构可覆盖**：ArrayById/DeepObject 合并，mod 改一个档案的字段不整表替换。
- **档案缺省即回退**：没有匹配档案时界面按引擎内建布局渲染，不因档案缺失失败。

## 3. 运行行为

技能聚合档案在安装期编译为分组器，供命令面板归组技能；命令甲板/生产总览档案加载后面板按档案参数渲染。

## 4. 异常承诺

groupBy 表达式未知、档案结构非法——加载失败并指明条目与位置；档案缺失本身不是错误（回退内建）。

**相关文档**：[配置说明](../config/infra-04-ui-profiles.md) · [ab-06](ab-06-slots.md) · [misc-04](misc-04-entity-info.md)
