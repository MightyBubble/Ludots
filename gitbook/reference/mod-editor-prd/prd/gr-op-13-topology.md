# gr-op-13 · 节点：拓扑谓词

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/gr-op-13-topology.md)；编辑器需求见 [UXD](../uxd/gr-op-13-topology.md)；引擎实现见 [runtime spec](../spec-runtime/gr-op-13-topology.md)；editor spec 见 [editor spec](../spec-editor/gr-op-13-topology.md)；现状见 [reference](../reference/gr-op-13-topology.md)。

## 1. 定位

指挥与信息拓扑的三件谓词：实体归到哪个控制域代表、谁能指挥谁、观众对实体有没有知识投影。多玩家指挥权与战争迷雾判定的图面基石。

## 2. 产品承诺

- **归属解析**：ControlDomainResolve 把成员实体归到控制域代表（如队长），输出代表实体。
- **指挥判定**：ControlDomainControls 判 a 能否指挥 b，输出 Bool。
- **知情判定**：KnowledgeHasProjection 判观众（a）对目标（b）是否有知识投影——迷雾内外的统一问法。
- **纯读通用**：三件不改任何状态，线性四类图全可用。

## 3. 运行行为

三件各自查一次拓扑结构（控制域树/知识投影表）出值；与 LoadViewer 组合可表达"从观众这侧看得见吗"。

## 4. 异常承诺

实体不在任何控制域——解析出无效句柄不报错；指挥/知情判定对无效实体返回假。引脚类型不符——编译失败。

**相关文档**：[配置说明](../config/gr-op-13-topology.md) · [gr-op-01](gr-op-01-context.md) · [fx-19](fx-19-vision.md) · [节点画廊 wiki](../../graph-node-op-wiki/README.md)
