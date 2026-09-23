# Minimap web shell showcase UAT

## 1. Overview
A new player uses a floating circular minimap frame. Native code still owns markers; the web shell only frames and focuses.

## 2. Structure
- One minimap web-shell panel
- Profile points at the composited overlay runtime mod
- One DataPlane topic

## 3. Details
Click-to-focus goes through a registered command. Markers stay native.

## 4. Scenarios

```gherkin
Feature: Minimap web shell first screen
  Scenario: Player focuses the map from the shell
    Given the player enters the minimap web shell showcase
    When the first screen appears
    Then a circular minimap frame is visible
    When the player clicks inside the frame
    Then the camera focus is submitted through a registered WebUI command
    And the browser does not draw its own gameplay markers
```

## 5. Boundaries
Generic Panel Kit code must not hardcode map or unit names.

## 6. UAT
Covered by the scenario above and BrowserMinimapWebShellShowcaseAcceptanceTests.