# Configurable Data Schema Acceptance

## Scenario

作者只提供 JSON schema 和 records，不新增 C# 类型；引擎加载嵌套 struct、数组、enum，并让面板读取其中的路径。

## Evidence

- `DataSchemaTests.Load_NestedStructArrayAndEnum_ProvidesValidatedRecordAndPath`
- `DataPanelProjectionTests.DataPin_ReadsNestedRecordWithoutGraphOrSkinCoupling`
- `DataPanelProjectionTests.MixedGraphAndDataPins_ReadFromIndependentSources`
- `DataPanelProjectionTests.DataPin_MissingPath_FailsWithContext`

## Result

配置加载、结构校验、路径投影和 Graph/数据混合面板均通过；未知字段、缺少必填字段、未知 enum、循环 struct 和缺失路径均明确失败。
