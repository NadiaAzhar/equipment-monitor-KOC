# AI Credit budget and usage

Fill in this sheet before you build, update it at the mid-point check, and complete it at the end. Your trainer uses it to score **Plan** and **Token efficiency**.

## 1. Budget per phase

Complete the budget before you start building, and explain each model choice. The [README](../README.md#-ai-credit-budget) has an example budget from the kickoff.

| Phase | Copilot feature | Model choice and why | Budget (AI Credits) | Actual at mid-point | Actual at end |
|---|---|---|---:|---:|---:|
| Plan | Plan mode / Ask | Frontier (Claude Opus): rule semantics, edge cases and status precedence are where a wrong call costs most later | 150 | | |
| Scaffold | Agent mode + instructions file | Low-cost: `dotnet new` projects and references are boilerplate | 80 | | |
| Build features | Agent / Edit mode | Mixed: frontier for the rules engine and rolling window; low-cost for import, endpoints and dashboard | 500 | | |
| Tests + review | Skill for tests, chat review | Low-cost with the `edge-case-tests` skill for test writing; one frontier Reviewer pass | 170 | | |
| Docs + demo | Ask, README generation | Low-cost: summarising existing work | 50 | | |
| **Total** | | | **950** | | |

**Variance at the end** = (actual − budget) ÷ budget = ____ %

Solid is within ±30% of budget; Strong is within ±15%.

If you can't measure each phase directly, split your trainer's readings across the phases and explain how you did it.

## 2. Usage readings

Your trainer records each team member's AI Credit usage at the start of the build, at the mid-point check and at the end. Each reading is a running total that includes earlier use, so subtract the start reading to see what each person used during the build. Copy the readings here.

| Team member | Start of build | Mid-point | End | Used (end − start) |
|---|---:|---:|---:|---:|
| | | | | |
| | | | | |
| | | | | |
| | | | | |
| | | | | |
| | | | | |
| | | | | |
| | | | | |
| **Team total** | | | | |

## 3. Mid-point re-plan

- **Credits used so far, compared with the budget for the phases you've done:**
- **What's going better or worse than planned:**
- **What you'll change for the second half (scope, models, context or prompts):**
- **Updated budget for the remaining phases:**

## 4. Before and after optimization

Show one concrete change to a prompt, the context or the model that cut credits without hurting the output.

| | Before | After |
|---|---|---|
| **What you did** | | |
| **AI Credits used** | | |
| **Output quality (evidence)** | | |

**What changed, and why it worked:**
