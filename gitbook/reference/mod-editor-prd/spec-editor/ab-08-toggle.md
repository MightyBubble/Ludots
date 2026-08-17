# ab-08 editor spec · Toggle 技能

> 编辑器实现任务书。编辑器需求见 [ab-08 UXD](../uxd/ab-08-toggle.md)；引擎侧见 [runtime spec](../spec-runtime/ab-08-toggle.md)。

## 1. 概述

Toggle 面板实现：两态视图、activeEffects 编辑、回收缺口静态检测、回路图。

## 2. 设计

- **两态视图**：toggleSpec 投影为开态卡（tag + 效果列表）与关态卡（时间轴复用 ab-02 组件或瞬时开关）。
- **回收缺口检测**：静态扫描 activeEffects 模板的身份 tag 是否覆盖 toggleTag（含规则连带可达），不覆盖即黄条；判定与效果生命周期过期条件同源。
- **回路图**：toggleTag 的引用交叉（路由表 requiredAll、tag 规则、其他 toggle）投影为节点图。

## 3. 精确语义与不变量

- 面板可产出的 toggleSpec 形状 = 加载器接受的形状。
- 缺口判定与运行期回收路径同源（生命周期过期，非逐个撤销）。

## 4. 依赖接口与验收
- 消费：toggleSpec 加载校验、效果模板身份 tag 读取、时间轴编辑器组件。
- 验收：配一个含光环的 toggle，开/关各实测一次，效果随关回收；缺口用例在编辑器被提示。

**相关文档**：[ab-08 UXD](../uxd/ab-08-toggle.md) · [ab-08 runtime spec](../spec-runtime/ab-08-toggle.md)
