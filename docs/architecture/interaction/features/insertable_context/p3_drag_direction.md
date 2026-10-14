# P3 Drag Direction

> Superseded historical design note.

Drag-direction targeting must be modeled as explicit input geometry and interaction context data,
then committed through RFC-0065 profiles. It must not use retired formal Selection response
plumbing.

Current implementation references:

- `gitbook/architecture/graph-capability-status.md` section 3.3.2
- `src/Core/Input/Interaction/`
- `src/Core/Input/Orders/CommandIntentBufferDrainSystem.cs`

RFC-0065 keeps its decision text. It is not the current input contract.

Any new drag workflow needs data-driven geometry, filter, commit, and order contracts with
focused acceptance coverage.
