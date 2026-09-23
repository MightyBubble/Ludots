# Tech tree / progression showcase UAT

## 1. Overview
A new player opens a progression tree, sees which node is ready, and researches it through a real command.

## 2. Structure
- One techtree panel
- Descriptor backed by Progression ids
- One DataPlane topic

## 3. Details
Node status comes from ProgressionStateBuffer and requirement evaluation. No TechTreeStore.

## 4. Scenarios

```gherkin
Feature: Tech tree first screen
  Scenario: Player researches the next available node
    Given the player enters the tech tree showcase
    And a root progression node is already completed
    When the progression panel appears
    Then the next node shows as available
    When the player clicks that node
    Then research is submitted through the registered progression command
    And the browser does not keep its own tech state
```

## 5. Boundaries
Generic Panel Kit code must not hardcode tech names or create TechTreeStore.

## 6. UAT
Covered by the scenario above.