# Production overview showcase UAT

## 1. Overview
A new player watches queue progress and worker groups. The panel mirrors live production state.

## 2. Structure
- One production-overview panel
- Split layout for queue and workers
- One DataPlane topic

## 3. Details
Queue and worker buckets are projected from existing command/status/order sources.

## 4. Scenarios

```gherkin
Feature: Production overview first screen
  Scenario: Player watches a queue fill
    Given the player enters the production overview showcase
    When a production order is already in progress
    Then the overview shows queue progress
    And worker groups are visible without a technical report
    And completing the queue updates the same live topic
```

## 5. Boundaries
Generic Panel Kit code must not hardcode unit or building names.

## 6. UAT
Covered by the scenario above.