# Configurable Data Schema Showcase Acceptance

## Scenario

作者进入数据结构工作台，看到非空的 `unit.scout` 示例；修改坐标或稀有度后，右侧面板通过隔离 preview session 实时更新。切换 Graph / Data / Mixed 改变可见面板；故意填错 enum 时导出禁用并保留上一份合法投影。作者侧可从零定义 schema/record、用 schema 驱动表单（含数组、enum、EntityRef）并绑定数组下标路径；写回经 `DataSchemaModAssetWriter`。代码能力由独立 capability `DataSchemaAuthoringCapabilityMod` 提供，Showcase Shared 只带资产。

## Evidence

- `ConfigurableDataSchemaShowcaseAcceptanceTests.Workbench_LoadsNonEmptyScoutAndProjectsDataPins`
- `ConfigurableDataSchemaShowcaseAcceptanceTests.Workbench_DraftEditUpdatesProjectionSession`
- `ConfigurableDataSchemaShowcaseAcceptanceTests.Workbench_InvalidEnumDisablesExportAndKeepsLastGoodProjection`
- `ConfigurableDataSchemaShowcaseAcceptanceTests.Workbench_SourceModeSwitchesVisiblePanel`
- `ConfigurableDataSchemaShowcaseAcceptanceTests.Authoring_AddFieldBindPathAndSaveToMod`
- `ConfigurableDataSchemaShowcaseAcceptanceTests.Authoring_BuildScoutFromScratch_DefinesSchemasRecordAndBinding`
- `ConfigurableDataSchemaShowcaseAcceptanceTests.Authoring_FormFieldsIncludeNestedAndArrayPaths`
- `ConfigurableDataSchemaShowcaseAcceptanceTests.Authoring_InvalidBindingKeepsSaveDisabled`
- `ConfigurableDataSchemaShowcaseAcceptanceTests.Authoring_BindingPathHotAppliesToLivePanel`
- `DataSchemaModAssetWriterTests.Save_ValidDraft_WritesSchemasRecordsAndPanels`
- `DataSchemaModAssetWriterTests.Save_InvalidRecord_DoesNotWriteFiles`

## Result

Showcase Mod、Native/Web 入口、非空 schema/record、预览会话、作者四层编辑（从零 / 表单 / EntityRef / 数组路径）、独立 capability 与写回目标 Mod 的验收均通过。正式启动资产仍经 ConfigPipeline 加载；草稿只进入 `DataSchemaProjectionSession`，保存经 `DataSchemaModAssetWriter` 校验后写盘。画廊实机截图仍待有图形宿主的环境补录。

## Launch

```text
.\scripts\run-mod-launcher.cmd cli launch 'preset:configurable_data_schema_native_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:configurable_data_schema_web_raylib'
```
