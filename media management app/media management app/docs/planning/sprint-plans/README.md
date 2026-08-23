# Sprint local plans

Per-sprint **Plan Mode** output before Agent Mode implementation.

**Workflow:** [06-ai-execution-guide.md §2.0](./06-ai-execution-guide.md#20-mandatory-pre-sprint-workflow-git--plan-mode)  
**After each closed sprint:** [06 §2.1a — progress review](../06-ai-execution-guide.md#21a-post-sprint-progress-review-mandatory-after-each-closed-sprint) before the next Plan Mode.

| File | Sprint | Status |
| ---- | ------ | ------ |
| *(none committed)* | 1 | S1 executed without local plan — retro optional |
| `sprint-02-local-plan.md` | 2 | ✅ Complete — tagged `four-pillars-sprint-02` |
| `sprint-03-local-plan.md` | 3 | ✅ Complete — tagged `four-pillars-sprint-03` |
| `sprint-04-local-plan.md` | 4 | ✅ Complete — tagged `four-pillars-sprint-04` |
| `sprint-05-local-plan.md` | 5 | 🧊 **Frozen / cancelled** — do not resume. Audit: [settings-modernization/](../settings-modernization/README.md) |
| `poster-flash-surgical-fix.md` | — | **Next code work** — Library poster flash (not Sprint 8 split) |
| — | 0–4 | ✅ Complete. E4 sprints 5–10 frozen. |

## Template (copy for each sprint)

```markdown
# Sprint NN — Local plan

**Branch:** auto-torrent @ `<commit>`
**Date:**
**Source:** 05-sprint-timeline.md § Sprint NN

## Git baseline
- [ ] `git status -sb` reviewed
- [ ] On `auto-torrent`

## Scope IN / OUT
(paste from 05)

## Definition of Done
(paste checklist from 05)

## Files to touch
- …

## Test gate
(paste from BUILD.md)

## Risks / rollback
- …
```
