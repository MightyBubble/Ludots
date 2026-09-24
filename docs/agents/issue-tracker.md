# Issue tracker: GitHub

Issues and PRDs for this repository live in `MightyBubble/Ludots` GitHub Issues.
Use the authenticated `gh` CLI for issue reads, creation, comments, labels, and closure.

## Conventions

- Create: `gh issue create --repo MightyBubble/Ludots --title "..." --body-file <path>`.
- Read: `gh issue view <number> --repo MightyBubble/Ludots --comments`.
- List: `gh issue list --repo MightyBubble/Ludots --state open`.
- Comment: `gh issue comment <number> --repo MightyBubble/Ludots --body "..."`.
- Label: `gh issue edit <number> --repo MightyBubble/Ludots --add-label "..."`.
- Close: `gh issue close <number> --repo MightyBubble/Ludots --comment "..."`.

Do not close or rewrite a parent issue when publishing dependent implementation slices.

