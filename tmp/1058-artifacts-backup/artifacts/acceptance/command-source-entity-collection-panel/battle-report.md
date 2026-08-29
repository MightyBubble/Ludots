# Command Source Entity Collection Panel Battle Report

## Scenario Card
- Goal: prove the command-source entity collection panel reads the active collection and virtualizes a large entity list.
- Viewport: 1600x900 headless Skia UI capture.
- Command source: 72 deterministic entities across Spearman / Archer / Knight categories with id, name, and GAS attributes.

## Outcome
- success: yes
- verdict: the interaction showcase mounts a bottom-left command roster that renders category chips and dense unit tiles without composing every entity tile each frame.
- screenshot: `screens/interaction-selection-entity-collection.png`