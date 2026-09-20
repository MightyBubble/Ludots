# Scenario Card: road_network_showcase_strategy_matrix

## Intent
- Player goal: validate that one playable road-network showcase exposes distinct planner preferences, route-weight biases, and execution traits without welding policy into Core.
- Acceptance focus: planning slice sends all three columns toward East Gate to prove different corridor selection; execution slice sends all three toward Central Crossing to compare movement traits on the same corridor.

## Matrix
- Blue Vanguard: planner=`Grand Road` execution=`Vanguard` status=`Grand Road selected Direct corridor with 32 sampled point(s).` points=`32` max|y|=`0`cm biases(d/n/s)=`-4000/6000/6000` speed=`1.00` waypoint=`45` arrival=`75`
- Blue North Column: planner=`North Scout` execution=`Courier` status=`North Scout selected North corridor with 56 sampled point(s).` points=`56` max|y|=`10125`cm biases(d/n/s)=`18000/-6000/26000` speed=`1.22` waypoint=`30` arrival=`55`
- Blue South Column: planner=`South Guard` execution=`Siege Train` status=`South Guard selected South corridor with 56 sampled point(s).` points=`56` max|y|=`10125`cm biases(d/n/s)=`18000/26000/-6000` speed=`0.68` waypoint=`34` arrival=`80`

## Movement Slice
- Execution target: `Central Crossing (0,0)` so courier / vanguard / siege share the same corridor family while only execution traits differ.
- Blue Vanguard advance after 180 ticks: `3416cm`
- Blue North Column advance after 180 ticks: `2790cm`
- Blue South Column advance after 180 ticks: `2705cm`

## Outcome
- success: yes
- verdict: showcase-owned planner weights produce different corridor choices, and execution presets produce visibly distinct movement envelopes on the same authored follow order.
