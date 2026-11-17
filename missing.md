# Review: what's missing (main @ fa67431, up to date)

## Done

- DB import (dedupe, BAD kept, late data sorted, psi→bar), 3 rules, status, config-driven limits.
- API: /api/sites, /api/equipment, /api/equipment/{id}, /api/equipment/{id}/alerts, /api/alerts, /api/import-report.
- Dashboard with site filter; 70 xUnit tests passing (rules, import, API, real-data golden check).
- Skills: data-import, api-endpoint, edge-case-tests (team-written). Instructions filled in.

## Missing

1. **README "Your solution"** – team name/members, stack, run/test commands, rule-to-test traceability table, Copilot retrospective, evidence checklist. (Trainers run the app from this.)
2. **docs/ai-credit-budget.md** – actuals (mid/end), usage readings per member, mid-point re-plan, before/after optimization example. Needs real numbers from the team.
3. **Chat review evidence** – no Reviewer-agent pass + fix commit yet (Strong level requires "Copilot asked to review and findings fixed").
4. **data-import profiler script** – `.github/skills/data-import/scripts/` is empty; the skill expects a profiler to be written on first run.
5. **End-to-end smoke check** – run the app, hit endpoints, view dashboard in browser before demo.

## Todos

- readme-solution: fill README solution section (needs team name/members from user)
- reviewer-pass: run Reviewer agent, fix findings, commit
- profiler-script: add profiler script to data-import skill
- smoke-check: run app + browser verification
- budget-sheet: fill budget actuals/re-plan (needs user numbers)
