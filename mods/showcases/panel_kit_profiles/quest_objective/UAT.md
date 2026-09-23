# Quest objective showcase UAT

## 1. Overview
A new player looks at the objective tracker and immediately knows what to do next.

## 2. Structure
- One objective panel
- Vertical list layout
- One DataPlane topic

## 3. Details
Objective text comes from Quest runtime projection.

## 4. Scenarios

```gherkin
Feature: Quest objective first screen
  Scenario: Player reads the next goal
    Given the player enters the quest objective showcase
    When the first screen appears
    Then the objective panel shows the current goal
    And the player can tell the next step without opening a tech dump
    And completing the stage updates the same live topic
```

## 5. Boundaries
Generic Panel Kit code must not hardcode quest names.

## 6. UAT
Covered by the scenario above.