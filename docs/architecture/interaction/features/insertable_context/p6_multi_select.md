# P6 Multi Select

> Superseded historical design note.

Multi-actor targeting and dispatch now use explicit command-source collections and
`CastDispatchProfile` routing. Player-facing "multi select" wording may remain as UI shorthand,
but implementation must use command actors and entity collections.

Current references:

- `docs/rfcs/RFC-0065-unified-interaction-collection-casting-architecture.md`
- `docs/audits/rfc_0065_pr581_workflow_closeout.md`
- `mods/showcases/interaction/InteractionShowcaseMod/`

Do not reintroduce retired response queues or selected-provider fallback.
