# Response Window And Context

> Superseded historical design note.

This file previously described an input-response path built on retired formal Selection APIs.
Current RFC-0065 implementation uses explicit entity collections, interaction frames, command
intent profiles, dispatch profiles, and `OrderQueue`.

Current references:

- `docs/rfcs/RFC-0065-unified-interaction-collection-casting-architecture.md`
- `docs/audits/rfc_0065_pr581_workflow_closeout.md`
- `src/Core/Input/Interaction/`
- `src/Core/Input/Orders/InputOrderMappingSystem.cs`

Do not copy old response-window examples from pre-retirement discussions. New targeting or
confirmation flows must publish explicit collections and fail fast when their expected collection
or profile is missing.
