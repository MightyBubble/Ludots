# Effect History Showcase UAT

```gherkin
Feature: Effect history and knowledge-safe target resolution

  Scenario: An effect uses the configured live target policy
    Given a viewer has the required Knowledge aspect for a subject
    And an effect is configured with the live target policy
    When the effect reaches its execution tick
    Then the record shows the configured live resolution result
    And the record includes the effect RootId and execution tick

  Scenario: An effect freezes the last-known value
    Given a viewer has a KnowledgeSnapshot containing a subject position
    And an effect is configured with the last-known target policy
    When the subject changes position after the snapshot expires
    Then the effect uses the stored snapshot value
    And the record does not read the current World position

  Scenario: A stale identity is rejected
    Given an EffectTargetRef stores a subject identity with a specific version
    When the subject is removed and the numeric Id is reused
    Then resolution returns a stale identity result
    And no Attribute or Tag change is applied to the replacement entity

  Scenario: Removal captures historical entity facts
    Given a subject has configured identity, position, Attribute and Tag fields
    When the subject is removed through the standard or direct destroy path
    Then an EntitySnapshot is captured before component removal
    And the snapshot remains readable without resolving the removed Entity

  Scenario: Source removal follows the effect policy
    Given a delayed effect stores a source identity and source snapshot
    When the source is removed before execution
    Then the effect is cancelled or continues according to its configured policy
    And the execution record keeps the original source snapshot

  Scenario: Knowledge permission without value is explicit
    Given a KnowledgeProjection allows an aspect
    And no matching value exists in KnowledgeSnapshot
    When the effect requests that aspect
    Then resolution returns a missing-value result
    And the current World is not queried as a fallback

  Scenario: Point and cell targets do not become entity targets
    Given an effect is configured with a Point or Cell target
    When no entity occupies the target location
    Then the configured no-result outcome is recorded
    And no nearby entity is selected implicitly

  Scenario: Attribute and Tag changes are historical
    Given an effect writes a configured Attribute delta and Tag change
    When the affected entity is removed afterwards
    Then the execution record retains the event tick and declared changes
    And readers do not reconstruct the values from the current World

  Scenario: The history store rejects capacity overflow
    Given the bounded snapshot or execution store is full
    When another record is requested
    Then the system returns an observable capacity error
    And no record is silently dropped

  Scenario: History is not a live query source
    Given a removed entity has a retained snapshot
    When a live EntitySet is evaluated
    Then the removed entity is absent
    And an explicit history reader can inspect the snapshot
```
