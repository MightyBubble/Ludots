# Scenario Card: chunk_streaming_showcase

## Intent
- Validate a standalone showcase mod for chunk window streaming without move-order gameplay coupling.
- Acceptance focus: moving the camera between authored landmarks changes loaded chunk signatures, node counts, and loaded road spline batches.

## Timeline
- start: camera=`0,0` chunks=`25` nodes=`187` splines=`11`
- east_gate: camera=`9000,0` chunks=`25` nodes=`163` splines=`11`
- red_capital: camera=`18000,0` chunks=`25` nodes=`95` splines=`6`
- reset_center: camera=`0,0` chunks=`25` nodes=`187` splines=`11`

## Outcome
- success: yes
- verdict: the chunk showcase exposes a readable camera-driven chunk window with road spline batches that shift as the camera moves.
