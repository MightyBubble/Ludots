# Response Window And Context

> Superseded historical design note.

This file previously described an input-response path built on retired formal Selection APIs.
Current flow uses explicit entity collections, interaction contexts, command intent profiles,
dispatch profiles, and `OrderQueue`. Orders are drained by `CommandIntentBufferDrainSystem`.

Current references:

- `gitbook/architecture/graph-capability-status.md` section 3.3.2
- `src/Core/Input/Interaction/`
- `src/Core/Input/Orders/CommandIntentBufferDrainSystem.cs`

RFC-0065 and `docs/audits/rfc_0065_pr581_workflow_closeout.md` keep their decision text. They are not the current input contract.

Do not copy old response-window examples from pre-retirement discussions. New targeting or
confirmation flows must publish explicit collections and fail fast when their expected collection
or profile is missing.
