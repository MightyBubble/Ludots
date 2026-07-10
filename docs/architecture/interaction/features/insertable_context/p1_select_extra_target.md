# P1 Select Extra Target

> Superseded historical design note.

The old version of this note described target insertion through retired formal Selection response
plumbing. Current targeting must use explicit entity collections owned by the active interaction
context and consumed through RFC-0065 command / cast profiles.

Use:

- `EntityCollectionStore`
- interaction context frames
- data-driven filter profiles
- `CastCommitProfile`
- `OrderQueue`

Do not add selected-provider fallback or implicit global target response queues.
