# DevOps Blazor

A personal, **read-only** Blazor app that runs on your machine next to Azure DevOps. It does two things the portal makes slow:

- **Pipelines at a glance.** Every pipeline's recent runs as a row of coloured dots, with pass rate, median duration, queue wait, and **problem labels** (Broken, Flaky, Slow, Getting slower, Long queue, Partially succeeded, Retried, Timed out, Warnings rising, Stale). Every run links straight to its log in Azure DevOps, including **"Open first failure"**.
- **Repo policy compliance.** Every repository's branch and repository policies checked against a baseline you define in JSON, shown as a matrix, with a per-repo scorecard and CSV and Markdown export.

The app never writes to Azure DevOps: the PAT has read-only scopes, and the HTTP layer refuses any request that isn't a `GET`. The plan behind it is in [docs/PLAN.md](docs/PLAN.md).

## Getting started

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

1. **Create a PAT** in Azure DevOps (*User settings → Personal access tokens*) with only these scopes:
   - *Code (Read)*: repositories and branch policies
   - *Build (Read)*: pipelines, runs and timelines
   - *Project and Team (Read)*: the project list
2. **Store your settings in user-secrets**, so the PAT never goes in a file in the repo:
   ```bash
   dotnet user-secrets set "AzureDevOps:Organization" "https://dev.azure.com/your-org" --project src/DevOpsBlazor.Web
   dotnet user-secrets set "AzureDevOps:Pat" "<your PAT>" --project src/DevOpsBlazor.Web
   dotnet user-secrets set "AzureDevOps:PatExpiresOn" "2027-01-31" --project src/DevOpsBlazor.Web   # optional, turns on the expiry warning
   ```
3. **Run it:**
   ```bash
   dotnet run --project src/DevOpsBlazor.Web
   ```
   Then open the URL it prints (http://localhost:5117). The **Status** page checks the connection and each scope.

## Configuration

Settings live in `src/DevOpsBlazor.Web/appsettings.json`. Secrets go in user-secrets.

| Setting | Default | What it does |
|---|---|---|
| `AzureDevOps:Organization` | – | Organisation URL, e.g. `https://dev.azure.com/contoso`. |
| `AzureDevOps:Projects` | all | Limit the app to these projects. |
| `AzureDevOps:RunsPerPipeline` | 20 | Runs fetched per pipeline for the list page. |
| `AzureDevOps:CacheMinutes` | 10 / 1 / 10 | How long pipelines, runs and policies are cached. **Refresh** in the header clears the cache. |
| `RunLabels:*` | see file | Thresholds for the problem labels (`SlowFactor`, `LongQueueMinutes`, `FlakyFlipsThreshold`, `StaleDays`…). |
| `Compliance:BaselineFile` | `compliance-baseline.json` | The compliance baseline. It's read on every page load, so edits apply after a Refresh. |

### Problem labels

Labels are hints, not verdicts. Hover a label to see the evidence behind it.

| Label | Meaning |
|---|---|
| Broken | The latest run on the default branch failed. Shows the streak, since when, and the first failing commit. |
| Flaky | The same commit both failed and passed, or results flip between pass and fail on one branch. |
| Slow | A run took more than `SlowFactor` × the median successful duration. |
| Getting slower | The newest third of successful runs is noticeably slower than the oldest third. |
| Long queue | The run waited more than `LongQueueMinutes` for an agent. |
| Partially succeeded | A step failed but the run carried on (`continueOnError`). |
| Retried / Timed out | From the run timeline: a stage or job was rerun, or a timeout message was logged. |
| Warnings rising | The average warnings per run is climbing (pipeline page). |
| Stale / Disabled | No runs for `StaleDays` days, or triggers are off. |

### Compliance baseline

`src/DevOpsBlazor.Web/compliance-baseline.json` lists the branches to check and the rules to apply, each with a severity:

```json
{
  "version": "2026-10",
  "branches": [ "default", "release/*" ],
  "rules": [
    { "id": "MinReviewers", "severity": "High", "min": 2 },
    { "id": "AllowedMergeTypes", "severity": "Low", "allowed": [ "squash" ] }
  ],
  "exemptions": [
    { "repo": "Platform/sandbox-*", "rules": [ "*" ], "reason": "Playgrounds", "until": "2027-01-01" }
  ]
}
```

| Rule | Checks |
|---|---|
| `MinReviewers` | An enforced minimum reviewer policy requiring at least `min` approvers (default 2). |
| `NoSelfApproval` | Authors can't approve their own pull requests. |
| `ResetVotesOnPush` | Votes reset when new changes are pushed, or approval is required on the latest iteration. |
| `BuildValidationBlocking` | A blocking build validation policy covering every path. Path-filtered policies give a warning. |
| `CommentResolution` | Comments must be resolved. An optional policy gives a warning. |
| `LinkedWorkItems` | A work item must be linked. An optional policy gives a warning. |
| `AllowedMergeTypes` | Only the listed merge types (`squash`, `noFastForward`, `rebase`, `rebaseMerge`) are allowed. |
| `PoliciesAreBlocking` | No enabled branch policy is optional. |
| `DefaultBranchIsMain` | The default branch is named `name` (default `main`). |
| `MaxFileSize` | An enforced file size restriction, optionally no bigger than `maxMB`. |

`"default"` means each repo's default branch. A pattern like `"release/*"` is only satisfied by a policy whose prefix scope covers *every* matching branch. Policies set project-wide (all repositories) and on each repo's default branch are resolved the same way Azure DevOps applies them. The repo page shows where each policy comes from.

## Development

```bash
dotnet build DevOpsBlazor.slnx
dotnet test DevOpsBlazor.slnx
```

| Project | Contents |
|---|---|
| `src/DevOpsBlazor.Core` | Domain models and all the logic with no I/O: run analysis and labels, the effective-policy resolver, compliance rules, report writers. |
| `src/DevOpsBlazor.AzureDevOps` | Read-only REST client (api-version 7.1): PAT auth, GET-only guard, paging, friendly errors, caching, and `AdoLinks`, the one place portal URLs are built. |
| `src/DevOpsBlazor.Web` | Blazor Web App (Interactive Server, Fluent UI). Pages are in `Features/`. |
| `tests/*` | xUnit tests for each project (bUnit for components, MockHttp for the client). |

To add a feature, add a client method in `DevOpsBlazor.AzureDevOps/Clients`, any logic in `DevOpsBlazor.Core`, and a page under `DevOpsBlazor.Web/Features`.
