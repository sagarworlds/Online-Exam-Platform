# Project management

Work is tracked on the GitHub project **[Online Exam Platform](https://github.com/users/sagarworlds/projects/2)**, which is used like a Jira Scrum board.

## Structure

| Jira concept | GitHub equivalent |
|---|---|
| Epic | Issue labelled `type: epic`, one per delivery milestone (M0–M9, requirements section 15) |
| Story / task / bug | Sub-issue of its epic, labelled `type: story` / `type: task` / `type: bug` |
| Fix version / release | Repository milestone (`M1 Foundation`, `M3 Authoring & enrollment`, …) |
| Workflow | `Status` field: Backlog → Todo → In Progress → In Review → Done |
| Sprint | `Sprint` iteration field (2 weeks, Sprint 1 starts 2026-10-05) |
| Priority | `Priority` field: P0 Critical (confirmed bugs), P1 High (MVP), P2 Medium, P3 Low (post-MVP) |
| Story points | `Story Points` number field |
| Components | `area: *` labels (one per module, plus `area: web` and `area: platform`) |

Story titles start with their requirement ID (for example `FR-50a …`), so the board can be filtered by requirement.

## Working agreement

- **Branches and PRs:** create the branch as `SP/<type>/<slug>` (requirements section 1.1). Put `Closes #<issue>` in the PR body so the automation moves the linked issue together with the PR.
- **WIP limits:** at most 3 cards in **In Progress** and 3 in **In Review**. Pull new work only when a slot is free.
- **Definition of done:** CI is green, the PR lists its FR/NFR IDs and assumptions, tests cover the change, and the docs are updated.

## Automation

`.github/workflows/project-automation.yml` adds every new issue and PR to the board and moves cards:

| Event | Card moves to |
|---|---|
| Issue opened / reopened / closed | Backlog / Todo / Done |
| Draft PR opened | In Progress |
| PR opened or marked ready for review | In Review |
| PR merged | Done |

PR events also move the issues the PR closes.

It needs the repository secret `PROJECT_TOKEN`: a classic personal access token with the `project` and `repo` scopes.

## One-time board setup (UI only; the GitHub API cannot do these)

1. **Board view (Scrum board):** open the project, then **+ New view → Board**, name it *Sprint board*, and set **Column by: Status**. Set the filter to `sprint:@current` so it shows only the current sprint.
2. **WIP limits:** in the board view, open each column's `…` menu → **Set limit**: In Progress = 3, In Review = 3. GitHub shows the count against the limit and highlights the column when it is exceeded. It does not block the move.
3. **Backlog view:** **+ New view → Table**, named *Backlog*. Group by **Milestone**, sort by **Priority**, and show the fields Status, Priority, Story Points, Sprint, Labels.
4. **Roadmap view:** **+ New view → Roadmap**, using the **Start date** / **Target date** fields, grouped by **Milestone**.
5. **Built-in workflows:** open **⋯ → Workflows** and enable *Item closed → Done*, *Pull request merged → Done* and *Auto-add sub-issues to project*. These complement the Actions workflow.
6. **Insights:** in **Insights**, create a *Burn up* chart filtered to `sprint:@current` to track sprint progress.
