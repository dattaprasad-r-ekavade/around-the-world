# Ember progress evidence

This file is the index of implementation evidence. The evidence itself lives in one file per
roadmap milestone under [`progress/`](progress/). Task status is kept only in the
[roadmap](ENGINE_ROADMAP.md); these files record what was done and how it was checked.

| File | Contents |
| --- | --- |
| [progress/M0.md](progress/M0.md) | M0 — reliability, soak, performance, persistence and CI evidence |
| [progress/M1.md](progress/M1.md) | M1 — project, asset, hierarchy, template and dirty-state evidence |
| [progress/UX.md](progress/UX.md) | UX — Home, workspace, visual actions, help and lesson evidence |
| [progress/M2.md](progress/M2.md) | M2 — colliders, triggers, Play settings and packaging evidence |
| [progress/E.md](progress/E.md) | E — editor project extraction evidence |
| [progress/L.md](progress/L.md) | L — declarative lessons and mission data evidence |
| [progress/roadmap-changes.md](progress/roadmap-changes.md) | Roadmap resets, direction decisions and source reviews |
| [progress/legacy-tasks-01-144.md](progress/legacy-tasks-01-144.md) | History of tasks 01–144 from the archived RPG roadmap (not current gates) |

Create a new milestone file (for example `progress/E.md`, `progress/L.md`, `progress/C.md`)
when its first evidence entry is written, and add it to this table.

## How to write an entry

Append a section at the end of the matching milestone file:

```markdown
## <Task ID> <short title> — <day month year>

What changed, in two to five sentences. Name the main types or files.

| Check | Result |
| --- | --- |
| <exact command or UI steps> | PASS/FAIL — <counts, commit, hardware if relevant, artifact path> |

Still open: <what this entry does not prove>.
```

Rules:

- Name the kind of evidence: code inspection, CPU test, live integration, visual/audio review,
  or relocated distribution check. A unit test cannot close a UI gate.
- Record failures and unrun checks as they are. Never report an unrun check as a pass.
- For a **Passed** task, record the commit, command or exact UI steps, fixture, configuration,
  hardware where relevant, result and artifact location.
- Update the task's status in ENGINE_ROADMAP.md in the same change.
