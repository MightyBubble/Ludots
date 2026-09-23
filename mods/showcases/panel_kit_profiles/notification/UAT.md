# Notification showcase UAT

## 1. Overview
A new player gets a toast when something finishes or goes wrong, and can act on it.

## 2. Structure
- One notification panel
- Toast stack layout
- One DataPlane topic

## 3. Details
Messages and actions come from Notification runtime, not quest/narrative private toast state.

## 4. Scenarios

```gherkin
Feature: Notification first screen
  Scenario: Player notices a completion toast
    Given the player enters the notification showcase
    When an important game event is published
    Then a toast appears with a localized message
    And the message is readable without a technical dump
    When the player clicks the toast action
    Then a registered WebUI command opens the related panel
```

## 5. Boundaries
Generic Panel Kit code must not hardcode game event names.

## 6. UAT
Covered by the scenario above.