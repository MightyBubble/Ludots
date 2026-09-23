# Resource bar showcase UAT

## 1. Overview
A new player opens this showcase and immediately sees a resource bar. Numbers come from the game, not from the browser inventing values.

## 2. Structure
- One resource panel
- One DataPlane topic
- Tokens for labels

## 3. Details
Player-facing labels are tokens. Field ids stay generic in Panel Kit code.

## 4. Scenarios

```gherkin
Feature: Resource bar first screen
  Scenario: Player reads stockpile at a glance
    Given the player enters the resource bar showcase
    When the first screen appears
    Then a resource bar is visible in the top-left
    And each number comes from the live attribute topic
    And the player can tell what they currently have without reading a tech note
```

## 5. Boundaries
Generic Panel Kit code must not hardcode game resource names.

## 6. UAT
Covered by the scenario above.