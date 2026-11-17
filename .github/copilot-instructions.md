# Project instructions

- The brief is in `README.md`, the data dictionary in `data/README.md` and the plan in `docs/plan.md`.
- Stack: C# on .NET 10, ASP.NET Core Minimal API, SQLite through Microsoft.Data.Sqlite (no ORM), xUnit with `Microsoft.AspNetCore.Mvc.Testing`. Front end is plain HTML/CSS/JS in `wwwroot`, no build step.
- Structure: `src/EquipmentMonitor.Core` (import, units, rules, status, repository), `src/EquipmentMonitor.Api` (`Program.cs` endpoints, `appsettings.json`, `wwwroot/` dashboard), `tests/EquipmentMonitor.Tests` (`Rules/`, `Import/`, `Api/`).
- API conventions: paths under `/api`, plural lower-case nouns (`/api/equipment/{equipment_id}/alerts`); JSON fields in snake_case matching the data; lists as `{"items","total","limit","offset"}`; times in ISO 8601 with `+03:00` (Kuwait); errors as `{"error":{"code","message","details"}}` with 400 for bad input, 404 for unknown ids, 500 with a generic message only.
- Code conventions: PascalCase types and members, one rule per class implementing `IAlertRule`; a rule returns an `Alert` carrying `RuleId`, the values and limit it used and a human-readable `Message`, or nothing. Limits, window size and unit factors come from data/config, never from code. Compare at full precision, never round before comparing.
- Tests: `dotnet test` from the repo root. Use the `edge-case-tests` skill when writing tests.
