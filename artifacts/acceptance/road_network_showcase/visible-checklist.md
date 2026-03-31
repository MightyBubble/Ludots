# Visible Checklist: road-network-showcase-command-and-chunking

- `000_start` should show the initial central loaded chunk window and visible road splines.
- `001_selected` should highlight Blue Vanguard as selected.
- `002_command_accepted` should show a cue marker near `0,0` and a valid accepted route HUD status instead of `error 2`.
- `003_column_advancing` should show the controlled blue column shifted east along the road.
- `004_chunk_shifted` should show the camera moved east and a different loaded chunk window.

- `000_start.png`: status=`Road command ready. Right-click near a road or fort.` selected=`Blue Vanguard` chunks=25 roads=9 cue=hidden
- `001_selected.png`: status=`Road command ready. Right-click near a road or fort.` selected=`Blue Vanguard` chunks=25 roads=9 cue=hidden
- `002_command_accepted.png`: status=`Grand Road selected Direct corridor with 17 sampled point(s).` selected=`Blue Vanguard` chunks=25 roads=28 cue=visible
- `003_column_advancing.png`: status=`Grand Road selected Direct corridor with 17 sampled point(s).` selected=`Blue Vanguard` chunks=25 roads=24 cue=hidden
- `004_chunk_shifted.png`: status=`Grand Road selected Direct corridor with 17 sampled point(s).` selected=`Blue Vanguard` chunks=25 roads=19 cue=hidden
