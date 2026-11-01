# Entity Selection Architecture

> Superseded historical document.

Formal Selection APIs are retired. This file is kept only so old links resolve.
Do not use it as an implementation reference.

Current architecture:

- Entity groups are stored in `EntityCollectionStore`.
- The player command group is the `commandSource` role in `Input/collection_keys.json`.
  The merged value is the collection name.
- Command authority must resolve actors from explicit entity collections and must fail fast
  when the expected collection or profile is missing.
- Order payloads must be self-contained, or explicitly refer to an owner / collection key /
  revision contract. They must not depend on an implicit global selected-provider path.
- User-facing "selection" wording is allowed only as plain UI shorthand for the default
  command-source collection.

Current reference documents:

- `docs/architecture/entity_collection_query_infrastructure.md`
- `gitbook/architecture/graph-capability-status.md` section 3.3.2 for aim, control schemes, and the retired input names

The RFC and the closeout audit keep their decision text. They are not the current input contract.

Retirement guard:

New code, config, tests, and docs must not reintroduce retired formal Selection services,
selection-specific request/response queues, or selected-provider fallback paths.
