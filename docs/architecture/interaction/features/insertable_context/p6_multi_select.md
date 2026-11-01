# P6 Multi Select

> Superseded historical design note.

Multi-actor targeting and dispatch now use explicit command-source collections and
`CastDispatchProfile` routing. Player-facing "multi select" wording may remain as UI shorthand,
but implementation must use command actors and entity collections.

Current references:

- `gitbook/architecture/graph-capability-status.md` section 3.3.2
- `mods/showcases/interaction/InteractionShowcaseMod/`

RFC-0065 and the closeout audit keep their decision text. They are not the current input contract.

Do not reintroduce retired response queues or selected-provider fallback.
