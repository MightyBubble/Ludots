# Epic #522 — Issue Tracker（仓库镜像）

GitHub Epic: https://github.com/MightyBubble/Ludots/issues/522  
RFC: [`RFC-0061-interaction-view-order-massnav-boundary-unification.md`](../rfcs/RFC-0061-interaction-view-order-massnav-boundary-unification.md)

Status: **Complete** (branch `cursor/ord-1-massnav-input-removal-18f0`, PR #535)

## Sub-issues

| Phase | ID | GitHub | Title | Status |
|-------|-----|--------|-------|--------|
| A | ORD-1 | [#523](https://github.com/MightyBubble/Ludots/issues/523) | Remove MassNavigationLocalCommandInputSystem | Done |
| A | ORD-2 | [#524](https://github.com/MightyBubble/Ludots/issues/524) | Remove MassNavigationSelectionSyncSystem | Done |
| A | ORD-3 | [#525](https://github.com/MightyBubble/Ludots/issues/525) | Relocate selection bind/clear out of MassNav | Done |
| A | ORD-4 | [#526](https://github.com/MightyBubble/Ludots/issues/526) | Extract SubmitMoveCommand to Order intake | Done |
| B | ORD-5 | [#527](https://github.com/MightyBubble/Ludots/issues/527) | Unified OrderQueue intake | Done |
| B | ORD-6 | [#528](https://github.com/MightyBubble/Ludots/issues/528) | Command semantic intent (#503) | Done |
| C | ORD-7 | [#529](https://github.com/MightyBubble/Ludots/issues/529) | EntityViewProfile SSOT | Done |
| C | ORD-8 | [#530](https://github.com/MightyBubble/Ludots/issues/530) | Migrate consumers to EntityView | Done |
| C | ORD-9 | [#531](https://github.com/MightyBubble/Ludots/issues/531) | Retire Selection dual-write | Done |
| D | ORD-10 | [#532](https://github.com/MightyBubble/Ludots/issues/532) | Update formal docs | Done |
| D | ORD-11 | [#533](https://github.com/MightyBubble/Ludots/issues/533) | Regression + test cleanup | Done |

## Acceptance (Epic #522)

- [x] Formation capability showcase playable：框选 + 右键 move 走 OrderQueue → ingestion → movement
- [x] MassNav Core 无 Input/Selection import（ingestion + nav components only；Input 系统由 CoreInputMod 注册）
- [x] 正式文档与 Epic issue 子项全部关闭（仓库镜像；GitHub issue 关闭待 merge 后人工确认）

## Related

- #503, #519
- 误创建测试 issue #521（可手动关闭）
