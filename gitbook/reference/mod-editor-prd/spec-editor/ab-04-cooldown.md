# ab-04 editor spec · 冷却三件套

> 编辑器实现任务书。编辑器需求见 [ab-04 UXD](../uxd/ab-04-cooldown.md)；引擎侧见 [runtime spec](../spec-runtime/ab-04-cooldown.md)。

## 1. 概述

冷却向导实现：时长换算、闭环联动写入、共享反向索引。

## 2. 设计

- **向导写入**：确认后原子写时间轴 TagClip 与 blockTags 两处（一次撤销单元）；改动同步维持两处一致。
- **共享索引**：全技能冷却 tag 反向索引，保存时增量更新。
- **换算显示**：tick↔秒只读换算取技能基准时钟（rt-01 同源表）。

## 3. 精确语义与不变量

- 闭环判定（TagClip 与 blockTags 成对）与运行期实际生效条件同源。
- 向导产出的条目与手写条目无差别（不引入平行 schema）。

## 4. 依赖接口与验收
- 消费：abilities.json 写管线、tag 注册表、时钟换算表。
- 验收：向导配 2 秒冷却实测生效；删除任一半时闭环缺口提示出现。

**相关文档**：[ab-04 UXD](../uxd/ab-04-cooldown.md) · [ab-04 runtime spec](../spec-runtime/ab-04-cooldown.md)
