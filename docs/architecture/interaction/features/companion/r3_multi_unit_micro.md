# R3: Multi-Unit Micro

## Mechanic

SC2-style multi-unit micro: box-select a group, then issue the same command to the selected set.

## Interaction Design

- Input: `Down`
- Selection: `Entities` via box selection
- Resolution: `Explicit` or `ContextScored`

## Implementation Notes

目标规则图是 `graph.aim.rule.unit_only`，拾取图是 `graph.aim.pick.hostile`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


- Formal multi-unit selection is stored in a selection container, not `SelectionGroupBuffer`.
- The same member truth can be reused by:
  - live player selection
  - formation/control-group views
  - command snapshot containers
- Attack/Move/Stop can broadcast to all current members without inventing a second group-truth structure.
- Each unit still executes its own order/runtime consequences independently.

## New Infrastructure Need

None in principle. The required base is now the container/member selection SSOT documented in `docs/architecture/entity_selection_architecture.md`.

## References

- StarCraft 2
- Age of Empires
- Company of Heroes
