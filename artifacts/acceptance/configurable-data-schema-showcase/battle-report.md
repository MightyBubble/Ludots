# Configurable Data Schema Showcase Acceptance

## Scenario

作者进入数据结构工作台，看到非空的 `unit.scout` 示例；修改坐标或稀有度后，右侧面板通过隔离 preview session 实时更新。切换 Graph / Data / Mixed 改变可见面板；故意填错 enum 时导出禁用并保留上一份合法投影。

## Evidence

- `ConfigurableDataSchemaShowcaseAcceptanceTests.Workbench_LoadsNonEmptyScoutAndProjectsDataPins`
- `ConfigurableDataSchemaShowcaseAcceptanceTests.Workbench_DraftEditUpdatesProjectionSession`
- `ConfigurableDataSchemaShowcaseAcceptanceTests.Workbench_InvalidEnumDisablesExportAndKeepsLastGoodProjection`
- `ConfigurableDataSchemaShowcaseAcceptanceTests.Workbench_SourceModeSwitchesVisiblePanel`
- `ConfigurableDataSchemaShowcaseAcceptanceTests.Workbench_WebSkin_HeadlessStillProjectsData`
- `DataSchemaProjectionSessionTests.TryPublishRecordDraft_ValidDraft_SwitchesPreviewAndBumpsRevision`
- `DataSchemaProjectionSessionTests.TryPublishRecordDraft_InvalidDraft_LeavesActiveUnchanged`

## Result

Showcase Mod、Native/Web 入口、非空 schema/record、预览会话、作者四层编辑（Schema/Record/Binding/Preview）与写回目标 Mod 的验收均通过。正式启动资产仍经 ConfigPipeline 加载；草稿只进入 `DataSchemaProjectionSession`，保存经 `DataSchemaModAssetWriter` 校验后写盘。

## Launch

```text
.\scripts\run-mod-launcher.cmd cli launch 'preset:configurable_data_schema_native_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:configurable_data_schema_web_raylib'
```
