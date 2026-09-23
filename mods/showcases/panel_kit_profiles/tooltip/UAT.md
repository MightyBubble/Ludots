# Tooltip showcase UAT

## 1. Overview
A new player focuses something and gets a readable explanation. Text comes from tokens, not hardcoded English in Panel Kit.

## 2. Structure
- One tooltip panel
- Rich-text token sections
- One DataPlane topic

## 3. Details
Title, body, badges, and tips are tokenized.

## 4. Scenarios

```gherkin
Feature: Tooltip first screen
  Scenario: Player understands a hovered target
    Given the player enters the tooltip showcase
    When the player focuses a target that has a tooltip binding
    Then a rich tooltip appears near the cursor
    And the text explains what the target does in plain language
    And missing tokens fail instead of showing empty filler
```

## 5. Boundaries
Generic Panel Kit code must not hardcode unit or ability names.

## 6. UAT
Covered by the scenario above.