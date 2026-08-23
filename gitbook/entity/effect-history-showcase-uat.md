# Effect History Showcase UAT

```gherkin
Feature: A player can understand delayed effect target resolution
  The player uses the effect_history_raylib showcase and reads the panel and world markers.

  Scenario: The first screen explains the playable loop
    Given the player starts the Effect History showcase
    Then the screen shows a cyan source, an amber target, and the control line
    And the control line names policy selection, submission, expiry, removal, and reuse

  Scenario: A live policy resolves the current identity
    Given the target is alive and the player selects Live with key "1"
    When the player presses Enter
    And waits until the pending line completes
    Then the panel shows a Resolved result
    And the history line includes the target id, world, version, root id, and execution tick

  Scenario: A last-known policy remains readable after knowledge expires
    Given the player selects LastKnown with key "2"
    When the player presses Enter and then presses H before execution
    Then the target marker changes to the violet last-known state
    And the panel shows the knowledge expiry tick
    And the completed record shows Stale instead of silently reading the current world

  Scenario: Removing and reusing an identity does not redirect an effect
    Given the player has a pending Live effect
    When the player presses R and then U before execution
    Then the old target is shown as not alive
    And the replacement is not treated as the old target
    And the completed record shows Stale for the original identity

  Scenario: Point and cell policies remain explicit spatial targets
    Given the player selects Point with key "3" or Cell with key "4"
    When the player presses Enter
    Then the world shows the explicit green spatial marker
    And the record keeps the selected policy
    And no nearby entity is selected implicitly

  Scenario: Runtime delay and knowledge TTL are visible controls
    Given the player is on the main scene
    When the player changes delay with "D" or "F"
    And changes TTL with "T" or "G"
    Then the panel immediately shows the new tick values
    And the next execution record contains those values

  Scenario: A failure remains visible to the player
    Given the player submits an effect whose identity or knowledge is no longer valid
    When the execution tick arrives
    Then the panel shows the explicit resolver result
    And the history list retains the failed record
    And no replacement or nearby entity is affected

  Scenario: An invalid lifecycle action remains visible
    Given the target is still alive
    When the player presses U before pressing R
    Then the panel says that reuse was rejected because the original identity is still alive
```
