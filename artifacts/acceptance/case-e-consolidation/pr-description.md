Case E 在万人场景中完整收集候选单位，支持镜头缩放和玩家切换；快速松开鼠标会清除矩形及黄色预览，切换玩家会隐藏旧玩家的标记，切回来恢复原有选择。

此 PR 包含此前 Case E 查询、万人场景、快速释放和 Presenter 整合分支的完整变更，目标为 main，已同步 `11a841fb40`。查询与输入复用现有管线，出生直接指定阵营和玩家归属；Presenter 通过显式 `activationCondition` 和集合可见参数驱动保留实例，条件随当前操控玩家动态更新。

PI Opus 5 与 PI DeepSeek v4 Flash 交换意见后统一命名：`selection_marker`、`selection_preview_marker`、`box_select_rectangle`、`selection_rules`、`case_e.selection_preview`、`case_e.marker.visible`。保留跨系统术语 `SolePossessedRep` 和主资产槽 `body`，删除两处被激活条件覆盖的默认值。配置、图、输入引用、测试与逐行注释版同步更新。

查询节点的 CI 修复单独提交为 `c0f9a299ab`：补两页生成文档、校正注册表图类型顺序。两项失败来自修改前的 [CI run 34217950139](https://github.com/MightyBubble/Ludots/actions/runs/34217950139)。新节点页面明确注明尚未录制演示录像。

验证：

- 命名后 Presenter 专项 26/26；一万单位、三万保留实例，20 次切换均值 Mesh 4.79 ms、HUD 6.05 ms，均为 0 B。重复清除和恢复保持实例身份、数量和组件结构。
- 同步最新 main 后，Release GAS 门禁、Case E、区域与场成员回归 **822/822**。此前缺失的地形子模块已按仓库锁定版本初始化。
- 文档校验通过；注册表校验错误 0，既有完备性警告 30；图节点生成器无漂移，注释版与生产 JSON 解析后相等。
- 此前独立 Raylib 验收：玩家一选择 4,089 个单位；切换前、切玩家二、切回的蓝色标记像素分别为 17,908、0、17,908。命名后未重新录制实机画面。

报告、性能与截图：[Case E 整合报告](https://github.com/MightyBubble/Ludots/blob/codex/case-e-fast-release/artifacts/acceptance/case-e-consolidation/REPORT.md)。完整配置逐行解释：[presenters.annotated.jsonc](https://github.com/MightyBubble/Ludots/blob/codex/case-e-fast-release/artifacts/acceptance/case-e-consolidation/presenters.annotated.jsonc)。

首次万人标记创建本轮为 50.7 ms，仍有分配；配置预留策略和任意谓词编译仍未完成。集合键参与存档指纹，旧名称配置的开发版存档按既有合同拒绝加载，不提供别名。历史 CSV 列名与测量证据保留原记录。
