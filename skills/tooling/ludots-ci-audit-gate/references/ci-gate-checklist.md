# CI Gate Checklist

- locate subject packet set
- verify required hook packets exist
- verify linked artifacts exist on disk
- verify visual subjects include review result or explicit blocked packet
- verify handoff and PR packet are consistent
- treat exhausted timeout budgets as failures, not as pending forever
- surface `*.blocked` packets explicitly in gate output
- write result summary and emit `ci.audit.completed`
