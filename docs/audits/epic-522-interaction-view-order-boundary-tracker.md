# Epic #522 — Issue Tracker（仓库镜像）

GitHub Epic: https://github.com/MightyBubble/Ludots/issues/522  
RFC: [`RFC-0061-interaction-view-order-massnav-boundary-unification.md`](../rfcs/RFC-0061-interaction-view-order-massnav-boundary-unification.md)

## Sub-issues

| Phase | ID | GitHub | Title |
|-------|-----|--------|-------|
| A | ORD-1 | [#523](https://github.com/MightyBubble/Ludots/issues/523) | Remove MassNavigationLocalCommandInputSystem |
| A | ORD-2 | [#524](https://github.com/MightyBubble/Ludots/issues/524) | Remove MassNavigationSelectionSyncSystem |
| A | ORD-3 | [#525](https://github.com/MightyBubble/Ludots/issues/525) | Relocate selection bind/clear out of MassNav |
| A | ORD-4 | [#526](https://github.com/MightyBubble/Ludots/issues/526) | Extract SubmitMoveCommand to Order intake |
| B | ORD-5 | [#527](https://github.com/MightyBubble/Ludots/issues/527) | Unified OrderQueue intake |
| B | ORD-6 | [#528](https://github.com/MightyBubble/Ludots/issues/528) | Command semantic intent (#503) |
| C | ORD-7 | [#529](https://github.com/MightyBubble/Ludots/issues/529) | EntityViewProfile SSOT |
| C | ORD-8 | [#530](https://github.com/MightyBubble/Ludots/issues/530) | Migrate consumers to EntityView |
| C | ORD-9 | [#531](https://github.com/MightyBubble/Ludots/issues/531) | Retire Selection dual-write |
| D | ORD-10 | [#532](https://github.com/MightyBubble/Ludots/issues/532) | Update formal docs |
| D | ORD-11 | [#533](https://github.com/MightyBubble/Ludots/issues/533) | Regression + test cleanup |

## Suggested implementation order

1. #526 ORD-4（Order intake 提取）与 #523 ORD-1（删 MassNav input）可并行，但 showcase 需两者齐备才绿
2. #524 ORD-2 依赖 #526
3. #525 ORD-3 可与 Phase A 其他项并行
4. #527 ORD-5 在 #523/#526 之后
5. Phase C 在 Phase A+B showcase 绿后再开
6. #532 / #533 收尾

## Related

- #503, #519
- 误创建测试 issue #521（可手动关闭）
