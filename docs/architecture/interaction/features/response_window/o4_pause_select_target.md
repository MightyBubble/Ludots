# O4 Pause Select Target

> Superseded historical design note.

Pause-and-confirm targeting is now an RFC-0065 context / collection / commit-profile workflow.
The old formal Selection response approach is retired.

Current references:

- `docs/rfcs/RFC-0065-unified-interaction-collection-casting-architecture.md`
- `docs/audits/rfc_0065_pr581_workflow_closeout.md`
- `mods/showcases/superweapon_context/SuperweaponContextShowcaseMod/`

New implementations must publish explicit target collections, expose readable UAT evidence, and
fail fast when the context or target collection is missing.
