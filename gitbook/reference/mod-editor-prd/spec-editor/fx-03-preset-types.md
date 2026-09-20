# fx-03 editor spec · Preset 类型系统

> 编辑器实现任务书。编辑器需求见 [fx-03 UXD](../uxd/fx-03-preset-types.md)；引擎侧见 [runtime spec](../spec-runtime/fx-03-preset-types.md)。

## 1. 概述

preset 类型面板：原型字典、消费者索引、mod 原型新建表单。

## 2. 设计

- 清单与详情为注册表投影；消费者索引由效果表反向扫描构建，保存时增量更新。
- 新建表单按分片表 schema 生成，handler 二选一；写入 mod 的 preset_types 分片。
- 效果编辑器的 presetType 下拉与选择器共用同一注册表数据源（fx-02 表单）。

## 3. 精确语义与不变量

- 面板原型集合与引擎注册表一致；内建/mod 徽标按 id 段判定。
- 新建原型的字段校验与 loader 同源。

## 4. 依赖接口与验收

- 消费：preset 类型注册表枚举、效果表加载产物、分片表保存管线。
- 验收：新建原型重启后出现在下拉；零消费者原型有灰显提示；id 段用量与注册表一致。

**相关文档**：[fx-03 UXD](../uxd/fx-03-preset-types.md) · [fx-03 runtime spec](../spec-runtime/fx-03-preset-types.md)
