# P3 Drag Direction

> Superseded historical design note.

Drag-direction targeting must be modeled as explicit input geometry and interaction context data,
then committed through RFC-0065 profiles. It must not use retired formal Selection response
plumbing.

Current implementation references:

- `docs/rfcs/RFC-0065-unified-interaction-collection-casting-architecture.md`
- `src/Core/Input/Interaction/`
- `src/Core/Input/Orders/InputOrderMappingSystem.cs`

Any new drag workflow needs data-driven geometry, filter, commit, and order contracts with
focused acceptance coverage.
