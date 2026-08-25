# Presenter Config Closed-Set Rejection (UnknownField)

## Scope
- `src/Core/Presentation/Config/PresenterDefinitionConfigLoader.cs`
- Unknown-field errors are a single structured contract: `code=UnknownField path=<path> field=<field> allowed=<allowed fields>`.
- Arrays render as `[index]`, objects as `.field`; definition root is `presenters.json[<id>]`.
- Closed-set validation covers: definition top level, every BehaviorSlot payload object, children entries (including `instanceChildren` sub-entries, `overrides`, `overrides.transform`, `instanceBehaviors`), rule / condition / event / command objects, extension `execution` and its trigger objects.
- Top-level closed set of each merged entry runs before extends/default merging (`Load` validates `mergedByKey` before `ExpandDefinition`), so an unknown field is always attributed to the entry that declares it.
- Full recursive validation runs inside `ParseDefinition` before `_registry.Register`, so a failing load never publishes a degraded `PresenterDefinition` (`RegisteredIds` stays empty).

## Validation matrix
| Layer | Failure test | Reported path |
| --- | --- | --- |
| Definition top level | `Load_RejectsUnknownDefinitionFields` | `presenters.json[typo_definition]` |
| Lifecycle / anchor / visibility | `Load_RejectsUnknownLifecycleFields`, `Load_RejectsUnknownAnchorFields` | `presenters.json[<id>].lifecycle` / `.anchor` |
| BehaviorSlot entry | `Load_RejectsUnknownBehaviorSlotFields` | `presenters.json[typo_behavior].behaviors[0]` |
| BehaviorSlot payload | `Load_RejectsUnknownAssetBindingFields`, `Load_RejectsCaseMismatchedCommandFieldAsUnknown` | `presenters.json[<id>].behaviors[0].assetBinding` |
| bindings / paramDefaults | `Load_RejectsUnknownBindingFields`, `Load_RejectsUnknownParamDefaultFields` | `presenters.json[<id>].bindings[0]` / `.paramDefaults[0]` |
| children item | `Load_RejectsUnknownChildFields`, `Load_RejectsUnknownChildOverrideKeys` | `presenters.json[<id>].children[0]` / `.children[0].overrides` |
| instance sub-entries | `Load_RejectsUnknownInstanceChildFields`, `Load_RejectsUnknownInstanceBehaviorPayloadFields` | `presenters.json[root].children[0].instanceChildren[0]` / `...instanceChildren[0].instanceBehaviors[0]` |
| rule / event / command / condition | `Load_RejectsUnknownRuleFields`, `Load_RejectsUnknownRuleEventAndCommandFields`, `Load_RejectsUnknownConditionFields` | `presenters.json[<id>].rules[0]` / `.rules[0].event` / `.rules[0].command` / `.rules[0].condition` |

## Regression guarantees
- First-error-in-document-order: `Load_ReportsFirstUnknownFieldInDocumentOrder` asserts the first unknown field in JSON document order is reported and later ones are not.
- Pre-merge attribution: `Load_ValidatesMergedEntryBeforeExtendsMerge` asserts an unknown field on the parent entry is reported at the parent path (`presenters.json[base_with_typo]`), never re-attributed to the extending child.
- Real fixture bubbling: `Load_RejectsUnknownFieldsInRealFixtureAndRegistersNothing` injects a typo into `mods/fixtures/presenter_schema_reference/.../presenters.json` `behaviors[0].assetBinding` and asserts the full path plus empty `RegisteredIds`.
- No degraded presenter: `Load_FailureLeavesRegistryWithoutPresenterDefinitions` asserts `RegisteredIds` is empty after a failed load.
- Legal configs still load: `Load_AcceptsLegalConfigAcrossAllEntryPoints`, `Load_AcceptsReservedAuthoringMetaFields`, `Load_ParsesChildInstanceSubtreeOverrideWithoutTouchingSharedDefinition`.

## Residual ignore/warn branches
- Searched the loader for ignore/warn-and-continue paths: none remain. Every object parse starts with `RejectUnknownFields` against a whitelist array; `overrides` / `overrides.transform` / extension `execution` / `execution.trigger` were the last ad-hoc loops and now route through the same helper.

## Test results (branch `codex/issue-1090-closed-set-reject`, pre-merge with origin/main)
- `dotnet test src/Tests/PresentationTests/PresentationTests.csproj -c Debug --filter "FullyQualifiedName~PresenterDefinitionConfigLoaderTests"`: 158 passed, 0 failed.
- Same project, filter `PresenterTreeLifecycleTests|PresenterBehaviorKindTests`: 45 passed, 0 failed.
- Full `PresentationTests` project: recorded in `trace.jsonl`.

## Trace
- `trace.jsonl` holds one JSON line per verification step.
