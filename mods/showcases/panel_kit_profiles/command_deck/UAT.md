# Command deck showcase UAT

## 1. Overview
A new player sees a command deck and can press a slot. The click becomes a registered WebUI command, not browser-local fake state.

## 2. Structure
- One command-deck panel
- Global display profile
- One DataPlane topic

## 3. Details
Slot labels and routes come from command panel sources configured by the showcase profile.

## 4. Scenarios

```gherkin
Feature: Command deck first screen
  Scenario: Player issues one global command
    Given the player enters the command deck showcase
    When the first screen appears
    Then a command deck is visible at the bottom
    When the player clicks an available slot
    Then the click is submitted through a registered WebUI command
    And the browser does not invent its own command queue
```

## 5. Boundaries
Generic Panel Kit code must not hardcode unit or ability names.

## 6. UAT
Covered by the scenario above.