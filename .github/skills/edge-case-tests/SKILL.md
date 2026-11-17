---
name: edge-case-tests
description: Writes xUnit edge-case tests for an alert rule, the CSV import or an API endpoint in this repo. Covers limits exactly at the boundary, BAD readings, rolling windows with too few GOOD readings, gaps, late and duplicate readings, unit conversion and API error shapes. Use when adding or changing a rule, the import or an endpoint, or when asked to write tests.
---

# Edge-case tests

Write focused xUnit tests in `tests/EquipmentMonitor.Tests`, in the folder that matches the code: `Rules/`, `Import/` or `Api/`. Attach only the file under test and this skill; don't load the data files into the chat.

## Conventions

- One behaviour per test. Name tests `Method_Condition_Expected`, for example `HighWarning_ValueExactlyAtLimit_RaisesAlert`.
- Build readings with the `TestData` helpers (`TestData.Limit(...)`, `TestData.Readings(...)`). Never hard-code a limit inside production code to make a test pass: limits come from `SensorLimit` / config.
- Use limits that make boundaries exact in binary floating point where possible, and also include the brief's `7.1` and `7.06` cases.
- Assert the alert's `RuleId`, `Value`, `Limit` and that `Message` names the rule, the sensor, the reading and the limit.
- Use `[Theory]` with `[InlineData]` for value tables instead of copying tests.

## Checklist for a rule

1. Value exactly at the limit raises an alert (`>=`); a value just below doesn't.
2. No rounding before comparing: `7.06` against a `7.1` limit raises nothing.
3. A `BAD` reading (for example `999.9`) raises nothing and doesn't count in a rolling window.
4. A reading in another unit is converted to the limit's unit first (`psi` → `bar`, 1 bar = 14.5038 psi): test just above and just below the limit after conversion.
5. Rolling window: 9 GOOD readings raise nothing, 10 can raise; a gap longer than the window resets it; readings older than 15 minutes leave the window.
6. A late reading (out of time order in the input) gives the same alerts as the in-order data.
7. A duplicate reading (same equipment, sensor, timestamp) is counted once.
8. Readings from another sensor or machine never mix into a window.

## Checklist for the import

- Exact duplicates go to `import_rejects` with reason `DUPLICATE`; row counts reconcile (`read = loaded + rejected`).
- `BAD` readings are kept, not rejected.
- An unknown sensor, unparseable value or unsupported unit is rejected with a reason, never dropped silently.

## Checklist for an endpoint

- `200` with the list envelope `{"items","total","limit","offset"}` and snake_case fields.
- Filters work (`site_id`, `rule`, `sensor`), and paging returns the right slice.
- Invalid filter or paging value → `400` with `{"error":{"code","message","details"}}`; unknown `equipment_id` → `404`.
- Times end in `+03:00`.

Run `dotnet test` from the repo root and make sure every new test fails without the behaviour it covers.
