# Visible Checklist: camera-acceptance-projection-click

- The `after_click` frame should show one more Dummy than `start` and a visible cue marker at the click point.
- The `marker_live` frame should still show the cue marker.
- The `marker_expired` frame should keep the new Dummy but remove the cue marker.
- `screens/timeline.png` gives a compact strip for side-by-side adapter review.

- `000_start.png`: dummy=0, cue=hidden
- `001_after_click.png`: dummy=0, cue=visible
- `002_marker_live.png`: dummy=1, cue=visible
- `003_marker_expired.png`: dummy=1, cue=hidden
