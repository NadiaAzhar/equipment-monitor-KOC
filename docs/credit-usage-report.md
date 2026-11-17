# AI Credit usage report: Nadia Azhar

Source: the Copilot session usage log for this repo's session (`4e5a183c`, 17 Nov 2026, 11:44–12:15 Kuwait time). Model: Claude Sonnet 5.5 (medium reasoning), request multiplier 1. Usage is recorded in AI units (`total_nano_aiu` ÷ 1e9). Confirm the AI unit to AI Credit conversion against your trainer's readings.

## Usage by prompt

| # | Prompt | Model calls | Input tokens | Output tokens | Cache read | Cache write | AI units |
|---|---|---:|---:|---:|---:|---:|---:|
| 1 | Project brief and status | 3 | 228,259 | 1,186 | 147,107 | 81,144 | 22.94 |
| 2 | Fix `&&` in the Run script | 4 | 331,028 | 728 | 247,820 | 83,198 | 24.01 |
| 3 | Use the full dotnet path | 3 | 252,553 | 766 | 251,031 | 1,514 | 3.66 |
| 4 | Fix NuGet restore (Sqlite 10.0.15, `nuget.config`) | 5 | 435,260 | 1,453 | 431,127 | 4,121 | 6.80 |
| 5 | This report | 4 | 364,420 | 1,489 | 271,556 | 92,854 | 27.42 |
| | **Total** | **19** | **1,611,520** | **5,622** | **1,348,641** | **262,831** | **84.83** |

The five prompts above are the whole session; row 5 was measured before this file was written.

## Mapping to the budget phases

| Phase | Work in this session | AI units |
|---|---|---:|
| Plan | none in this session (plan existed) | 0 |
| Scaffold | none | 0 |
| Build features | none | 0 |
| Tests + review | fixing the build so tests run (70 pass) | ~6.8 |
| Docs + demo | project brief, run-script fixes, this report | ~78.0 |
| **Total** | | **84.83** |

Against the 950-credit team budget this session is about 9%, so it doesn't count toward your per-member variance on its own. Other sessions (plan, build, tests) aren't in this log.

## Observations for the "before/after" section

- Most cost comes from the first call of a prompt, when the context is written to the cache (~80k tokens at the higher cache-write rate). Prompts 1, 2 and 5 paid for this; prompts 3 and 4 reused the cache and cost 3.7–6.8 units.
- Keeping follow-up requests in the same session and short, as in prompts 3 and 4, is the cheapest pattern here.
- A low-cost model would suit these small config and docs tasks.
