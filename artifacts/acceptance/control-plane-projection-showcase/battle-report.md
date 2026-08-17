# Scenario: control-plane-projection-showcase

## Header
- build: GasTests / ControlPlaneProjectionShowcase_RoutesSelectionByDomainAndProjectsOwnedVsProxiedMarkers
- seed: map-authored deterministic scenario
- map: control_plane_projection
- clock: engine fixed step sampled through 1/60s test ticks
- execution timestamp UTC: 2026-08-16T18:33:09.9806750+00:00
- launcher binding: control_plane_projection_showcase
- WebApp asset root: mods/showcases/control_plane_projection/ControlPlaneProjectionShowcaseMod/assets/control-plane-app/index.html
- DataPlane: topic ludots.showcase.control_plane.state, command toggleProxy

## Scenario Card
- Player goal: box-select a mixed P1/P2 squad, toggle proxy control with O, and see owned vs proxied marker buckets update.
- Gameplay domain: RFC-0065 SHOW-2 / M3 domain-routed collection writes + M4 viewer-relative projection + P5 WebUI dataplane.
- Initial entities: P1Rep, P2Rep, TeamRep, 5 P1 units, 3 P2 units.
- Action script: verify launcher/WebApp, load map, press O on, replace selection with one P1 + one P2 unit, press O off.
- Primary success condition: owned projection remains at 1, proxied projection becomes 1 under Controls grant, then clears after revoke.
- Failure branch condition: missing launcher/WebApp binding, missing Granted Controls edge, cross-domain command rows, or stale proxied projection.

## Timeline
[T+000] Preflight.Verify(launcher + packaged WebApp) -> Ready | binding=control_plane_projection_showcase | topic=ludots.showcase.control_plane.state
[T+004] Engine.LoadMap(control_plane_projection) -> ScenarioReady | P1Rep/P2Rep/TeamRep resolved
[T+008] RelationshipRuntime.EnsureLink -> Owns/MemberOf/Ally topology present | no Core fallback path
[T+016] P1Rep.Press(O) -> ProxyOn | Tag+participant.offline | Controls(P1Rep->P2Rep)+Granted
[T+024] EntityCollectionStore.Replace(P1 mixed CommandSource) -> DomainRoutedCollectionWriter split rows | P1=1 P2=1
[T+028] ControlPlaneView.Project(P1Rep) -> Marker buckets | owned=1 proxied=1
[T+040] P1Rep.Press(O) -> ProxyOff | Controls grant revoked | proxied marker bucket clears

## Outcome
- result: success
- headless evidence: launcher binding, packaged WebApp assets, DataPlane contract surface, O-key input path, profile-owned grant/revoke, and collection projection all passed.
- visible evidence boundary: no real raylib window or CEF browser was captured in this run; GUI recording remains manual environment work.
- manual GUI command to run: `.\scripts\run-mod-launcher.cmd cli launch control_plane_projection_showcase --adapter raylib`

## Summary Stats
- total player actions: 3 (O on, mixed selection commit, O off)
- owned_projection_after_proxy_on: 1
- proxied_projection_after_proxy_on: 1
- proxied_projection_after_revoke: 0
- dropped/budget/fuse counters: 0 observed in this headless acceptance path
