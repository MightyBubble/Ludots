# R3: Multi-Unit Micro

## Mechanic

SC2-style multi-unit micro: box-select a group, then issue the same command to the selected set.

## Interaction Design

- Input: `Down`
- Selection: `Entities` via box selection
- Resolution: `Explicit` or `ContextScored`

## Implementation Notes

```text
OrderSelectionType.Entities
  -> selection container resolved from the current viewed selection
  -> order fan-out or shared order planning consumes the container members

InputOrderMapping
  actionId: "MoveOrder"
  selectionType: Entities
  interactionMode: Explicit
```

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
