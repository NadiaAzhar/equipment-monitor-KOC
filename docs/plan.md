# Equipment Monitor: plan

## Problem and approach

A control room operator watches 36 sensors on 12 pumps and compressors and misses slow drifts and spikes. We load one day of 1-minute readings into SQLite, run three alert rules (`HIGH_WARNING`, `HIGH_ALARM`, `ROLLING_AVERAGE_HIGH`), work out each machine's status for the day, serve the results from a JSON API, and show them on a dashboard filtered by site.

**Stack:** .NET 10, ASP.NET Core Minimal API, Microsoft.Data.Sqlite, xUnit (+ `WebApplicationFactory` for endpoint tests), and a static HTML/JS dashboard in `wwwroot`. No front-end build step.

**Structure**

```
EquipmentMonitor.sln
src/EquipmentMonitor.Core/   domain: models, UnitConverter, rules, RollingWindow, StatusCalculator, CsvImporter, AlertRepository
src/EquipmentMonitor.Api/    Program.cs (endpoints, error handling), appsettings.json, wwwroot/ (dashboard)
tests/EquipmentMonitor.Tests/ Rules/, Import/, Api/
docs/plan.md                 copy of this plan (rubric evidence)
```

**Run flow:** `dotnet run --project src/EquipmentMonitor.Api` rebuilds `equipment.db` from `data/*.csv` at startup (~52k rows, takes seconds), evaluates the rules, stores the alerts, then serves the API and the dashboard. `dotnet test` runs everything.

## Data profile (measured)

| Issue | Found | Decision |
|---|---|---|
| BAD readings | 17, all `999.9` | Store them with `quality=BAD`. Every rule ignores them: no alert, and they don't count toward the rolling window. The dashboard shows a BAD count for each machine. |
| Exact duplicates | 120 rows | Dedupe on `(timestamp, equipment_id, sensor)`. No conflicting duplicates exist. If one ever appears, keep the first copy and log a warning. The import reports the counts. |
| Late-arriving backfill | Block at end of file (2 order breaks) | Sort by timestamp for each sensor before evaluating. Results don't depend on file order. |
| Gaps | Some sensors have 1,425 of 1,440 readings | Missing minutes are simply absent. Rolling windows are time-based, so a gap reduces the GOOD count. |
| Different unit | K-203 PRESSURE in `psi`, limit in `bar` | Convert every reading to its limit's unit before comparing: `bar = psi / 14.5038`. Conversion factors live in config. An unknown unit pair rejects the row and logs a warning. |
| UTC timestamps | All `Z` | Store in UTC. The API returns `+03:00` (Asia/Kuwait offset) and the dashboard shows local time. |

## Rules and acceptance criteria

Common to all rules: only `GOOD` readings, with the value converted to the limit's unit and compared at full precision (`double`, no rounding before the comparison). Limits come from `sensor_limits.csv` through the DB. Window size and minimum count are in `appsettings.json`. No limits in code.

**HIGH_WARNING:** the value is at or above `warning_high`.
- AC: 7.1 with limit 7.1 raises an alert. 7.0999 doesn't. 7.06 doesn't (no rounding). A BAD 999.9 doesn't. A psi reading is converted, and the alert shows both the converted and the original value.

**HIGH_ALARM:** the value is at or above `alarm_high`.
- AC: 11.2 with limit 11.2 raises an alert. A reading that breaks the alarm limit also raises `HIGH_WARNING` (one alert per rule broken, as the brief says).

**ROLLING_AVERAGE_HIGH:** at each GOOD reading at time t, average the GOOD readings in `[t-14 min, t]`. If there are at least 10 of them and the average is at or above `rolling_avg_high`, raise an alert.
- AC: average exactly at the limit raises an alert. 9 GOOD readings raise nothing (start of day, after a gap, or BAD readings in the window). A late reading is placed in its correct time position. Readings from other sensors or machines never mix in.

**Status:** `ALARM` if the machine has any `HIGH_ALARM`. Otherwise `WARNING` if it has any `HIGH_WARNING` or `ROLLING_AVERAGE_HIGH`. Otherwise `NORMAL`.

**Explainability:** each alert has a message such as `HIGH_ALARM: vibration 11.6 mm/s (alarm limit 11.2 mm/s) at 2026-10-14 09:15 (UTC+3)`. A rolling alert has its own message, for example `ROLLING_AVERAGE_HIGH: vibration 15-min avg 6.12 mm/s over 14 GOOD readings (limit 6.0 mm/s)`.

## API (conventions: snake_case fields, list envelope, one error format)

| Endpoint | Returns |
|---|---|
| `GET /api/sites` | Sites with machine counts |
| `GET /api/equipment?site_id=` | Machines with `status`, alert counts by rule, BAD count, sorted by status then criticality |
| `GET /api/equipment/{equipment_id}` | One machine with its limits and status. Unknown ID returns `404`. |
| `GET /api/equipment/{equipment_id}/alerts?rule=&sensor=&limit=&offset=` | `{items,total,limit,offset}`. Each alert has `rule_id, severity, sensor, timestamp(+03:00), value, unit, limit, original_value, original_unit, window_good_count, message`. |
| `GET /api/alerts?site_id=&rule=&limit=&offset=` | Alerts across all machines |

Errors use `{"error":{"code","message","details"}}`. An invalid `site_id`, `rule` or paging value returns `400`. An unknown machine returns `404`. Any other failure returns `500` with a generic message and no stack trace.

## Dashboard

Site filter (from `/api/sites`), machine cards coloured by status (ALARM red, WARNING amber, NORMAL green) with alert counts and BAD-data badge. Clicking a machine shows its alert table: time (Kuwait), rule, sensor, reading against limit, message. Every value comes from the API.

## Tests (xUnit)

- Rules: boundaries (at the limit, just below, 7.06 vs 7.1), BAD readings, psi conversion (just above and just below the limit in bar), a window with 9 vs 10 GOOD readings, a gap, a late reading out of order, a duplicate, and status precedence.
- Import: dedupe count, BAD readings kept, unknown unit rejected, row counts reconciled.
- Endpoints: 200 shapes, site filter, paging, 400 on a bad filter, 404 on an unknown machine, `+03:00` timestamps.
- Golden check against the real data: the alert counts for each machine are stable (snapshot).

## Copilot practices (rubric evidence)

- `.github/copilot-instructions.md`: fill in the TODOs (stack, structure, conventions, test command).
- New skill `.github/skills/edge-case-tests/SKILL.md`: the test prompt and the checklist above.
- Reviewer agent / chat review after the build. Log the findings and fixes in commits.
- Commit after each todo.

## Model and AI Credit budget per phase

| Phase | Feature | Model and why | Budget |
|---|---|---|---:|
| Plan | Plan mode | Frontier: rule semantics and edge cases are where mistakes cost most | 150 |
| Scaffold | Agent + instructions | Low-cost: boilerplate `dotnet new`, project refs | 80 |
| Build features | Agent / Edit | Frontier for rules and rolling window; low-cost for import, API and dashboard | 500 |
| Tests + review | edge-case-tests skill, Reviewer | Low-cost for writing tests; frontier for one review pass | 170 |
| Docs + demo | Ask | Low-cost | 50 |
| **Total** | | | **950** |

Mid-point check: record actuals in `docs/ai-credit-budget.md` §3 and re-plan. Candidate before/after: a scoped-context prompt with the skill versus a whole-repo prompt for generating tests.

## Team split (5–8 devs, parallel after scaffold)

A: import + DB · B: rules + status · C: API · D: dashboard (against the API contract with mock JSON first) · E: tests skill + tests · F: docs, budget, demo.

## Notes

- Positive limits only (all limits are `*_high`).
- Window size `15`, minimum GOOD `10` and unit factors are in `appsettings.json` under `Rules`.
- The DB file is git-ignored.
