# O4 Pause Select Target

> Superseded historical design note.

Pause-and-confirm targeting is an interaction context plus an explicit target collection.
The old formal Selection response approach is retired.

Current references:

- `gitbook/architecture/graph-capability-status.md` section 3.3.2
- `mods/showcases/superweapon_context/SuperweaponContextShowcaseMod/`

RFC-0065 and the closeout audit keep their decision text. They are not the current input contract.

New implementations must publish explicit target collections, expose readable UAT evidence, and
fail fast when the context or target collection is missing.
