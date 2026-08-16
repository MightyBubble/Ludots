# Scenario Card: road_network_showcase_timeout

## Intent
- Validate the showcase-owned timeout layer separately from path query and movement steering.
- Acceptance focus: one stalled road-follow order refreshes from its preserved final target; another stalled order abandons cleanly when refresh pathing fails.

## Branches
- Refresh branch: status=`Road route refreshed after timeout 1.` activeOrder=`True`
- Abandon branch: status=`Road route abandoned after timeout replan: refresh plan build was rejected.` activeOrder=`False`

## Outcome
- success: yes
- verdict: timeout handling proves both refresh and abandon branches without welding policy into Core.
