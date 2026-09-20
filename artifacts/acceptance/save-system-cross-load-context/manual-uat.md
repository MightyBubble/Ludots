# Manual UAT: Cross-Load-Context Save/Load

Feature: Player saves and reloads a named actor after the host reloads game assemblies

  Scenario: Saved named actor returns and the mission continues
    Given I am playing the core save showcase map
    And the actor "HAN Save Pilot" is visible at the northern gate
    And the mission objective says "Hold the northern gate"
    When I save the mission into a manual slot
    And the host reloads the game assemblies before reading the slot
    And I load that manual slot
    Then I see "HAN Save Pilot" with the same name and position as the save point
    And the mission objective says "Hold the northern gate"
    And actors created after the save point are gone
    And the mission continues without missing names, invalid entity references, or save errors

  Scenario: Incompatible component contracts stop the load
    Given I have a save slot from the core save showcase map
    When the saved component contract does not match the current component contract
    Then the load is rejected with a clear save error
    And no partial world is applied to the current mission