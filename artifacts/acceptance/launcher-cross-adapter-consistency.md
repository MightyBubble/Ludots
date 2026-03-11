# Launcher Cross-Adapter Consistency

## Scope

- `camera_acceptance` via `raylib`
- `camera_acceptance` via `web`
- `nav_playground` via `raylib`
- `nav_playground` via `web`

## Commands

- `.\scripts\run-mod-launcher.cmd cli launch camera_acceptance --adapter raylib --record artifacts/acceptance/launcher-camera-acceptance-raylib`
- `.\scripts\run-mod-launcher.cmd cli launch camera_acceptance --adapter web --record artifacts/acceptance/launcher-camera-acceptance-web`
- `.\scripts\run-mod-launcher.cmd cli launch nav_playground --adapter raylib --record artifacts/acceptance/launcher-nav-playground-raylib`
- `.\scripts\run-mod-launcher.cmd cli launch nav_playground --adapter web --record artifacts/acceptance/launcher-nav-playground-web`

## Result

- `camera_acceptance`
  - raylib: `camera_acceptance_projection_click|dummy:0->1|spawn:3200,2000|cue:110|camera:-189,-189`
  - web: `camera_acceptance_projection_click|dummy:0->1|spawn:3200,2000|cue:110|camera:-189,-189`
  - verdict: matched
- `nav_playground`
  - raylib: `navigation2d_playground_timed_avoidance|mid:1478/1478|final:5303/5305|center:14/128|stopped:0|peak:14@720`
  - web: `navigation2d_playground_timed_avoidance|mid:1478/1478|final:5303/5305|center:14/128|stopped:0|peak:14@720`
  - verdict: matched

## Evidence Directories

- `artifacts/acceptance/launcher-camera-acceptance-raylib/`
- `artifacts/acceptance/launcher-camera-acceptance-web/`
- `artifacts/acceptance/launcher-nav-playground-raylib/`
- `artifacts/acceptance/launcher-nav-playground-web/`

Each directory contains:

- `battle-report.md`
- `trace.jsonl`
- `path.mmd`
- `visible-checklist.md`
- `summary.json`
- `screens/*.png`
- `screens/timeline.png`
