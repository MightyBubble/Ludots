# Scenario: rfc0065-referee-projection-showcase

## Header
- build: GasTests / ControlPlaneRefereeProjectionShowcase_ProjectsTwoControlDomainsAndShrinksAfterRevoke
- seed: control_plane_projection map plus deterministic headless referee/foreign fixture rows
- map: control_plane_projection
- clock: immediate ControlPlaneView reads after RelationshipRuntime edge mutations
- execution timestamp UTC: 2026-08-17T05:54:16.7813259+00:00

## Scenario Card
- Player goal: referee observes one owned marker plus two proxied control domains, then revokes one proxied domain.
- Gameplay domain: RFC-0065 SHOW-3 referee / multi-control-domain projection headless evidence.
- Initial entities: RefereeRep, P1Rep, P2Rep, ForeignRep, and one command-source row in each domain.
- Action script: seed fixture rows, grant Controls(referee->P1Rep/P2Rep), read ControlPlaneView, revoke Controls(referee->P2Rep), read again.
- Primary success condition: projection returns owned=1 and proxied=2 before revoke, then owned=1 and proxied=1 after revoke.
- Failure branch condition: foreign domain row appears, revoked P2 row remains, or any row arrives without the expected domain provenance.

## Timeline
[T+000] Preflight -> reuse ControlPlaneView + RelationshipRuntime + EntityCollectionStore; no parallel projection path
[T+004] Fixture.Seed -> referee owned row + P1/P2 proxied rows + foreign row all present
[T+008] RelationshipRuntime.EnsureLink -> Controls(referee->P1Rep) + Controls(referee->P2Rep)
[T+012] ControlPlaneView.CopyMembersWithDomain(referee) -> owned=1 proxied=2 domains=3 foreign=0
[T+016] RelationshipRuntime.RemoveLink(referee->P2Rep) -> projection shrinks to owned=1 proxied=1 foreign=0

## Outcome
- result: success
- headless evidence: ControlPlaneView concatenated only the referee-owned domain and the two Controls-reachable domains; after revoke, the next read shrank without moving or deleting domain rows.
- visible evidence boundary: this scenario is headless projection evidence only; raylib marker recording remains separate visible UAT work.

## Summary Stats
- projected_rows_before_revoke: 3
- projected_rows_after_revoke: 2
- proxied_markers_before_revoke: 2
- proxied_markers_after_revoke: 1
- foreign_rows_returned: 0
