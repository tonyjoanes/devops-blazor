# DevOps Blazor: Plan

A personal, **read-only** Blazor app that runs locally next to Azure DevOps (ADO). It does two things the portal makes slow:

1. **Pipelines at a glance:** the status and duration of the last batch of runs for each pipeline, with likely problems labelled, and one click through to ADO for logs and details.
2. **Repo policy compliance:** a clear picture of which repos meet your branch policy baseline, which don't, and why.

It's built in small, shippable slices so new features are cheap to add.

> **Status (Oct 2026):** Phases 0–4 are built. See the README for setup.
> Where the build differs from this plan:
> - Domain models live in `DevOpsBlazor.Core`, so the logic and its tests don't depend on the HTTP client.
> - Caching uses a dedicated `IMemoryCache`, so **Refresh** can clear it.
> - Charts are small inline SVG components rather than ApexCharts.
> - Policy type names come from each policy's inline `type.displayName`, so `policy/types` isn't called.

---

## 1. Scope

**In scope (MVP)**
- Read-only views over pipelines and their recent runs: status, duration, queue wait and trends.
- Automatic **problem labels** on runs and pipelines (failing streak, flaky, slow, and so on).
- One-click deep links into ADO for logs, runs, pipelines and repo policy settings.
- Repo policy viewer plus a **compliance matrix** against a baseline you define, with export.

**Not now**
- Any write operation against ADO (queue, retry, cancel, approve, edit).
- Viewing log content in the app. Logs are opened in the ADO portal.
- Agent pool analytics. The research is kept in §6 for later.
- Hosting. The app runs locally (`dotnet run`, browse to `https://localhost`).

---

## 2. Technology choices

| Concern | Choice | Why |
|---|---|---|
| Framework | **.NET 10 (LTS), Blazor Web App**, **Interactive Server** render mode | The PAT stays on the server. Server-side HttpClient calls avoid CORS. Simple to run locally. |
| UI kit | **Microsoft Fluent UI Blazor** (`Microsoft.FluentUI.AspNetCore.Components`) | Looks native next to ADO. Includes `FluentDataGrid` (sort, filter, virtualise), badges, tooltips and dialogs. |
| ADO access | **Typed `HttpClient` against the ADO REST API (v7.1)** behind our own interfaces | Lightweight, easy to mock, and gives full control over paging and query parameters. |
| Resilience | `Microsoft.Extensions.Http.Resilience` | Retries and backoff, and honours `Retry-After` on ADO 429/503 throttling. |
| Caching | `HybridCache` (in-memory) | Repo, pipeline and policy lists change rarely (cache for minutes). Runs are cached for about a minute. |
| Charts | ApexCharts.Blazor | Duration sparklines and trends, compliance summary charts. |
| Export | CsvHelper plus a simple Markdown writer | Compliance evidence for audits or tickets. |
| Tests | xUnit + bUnit + `RichardSzalay.MockHttp` | Client tests against recorded JSON, rule tests, and component tests. |

### Authentication: PAT, read-only
- The PAT lives in `dotnet user-secrets`, never in `appsettings.json` or the repo. It's sent as Basic auth (`:{PAT}` base64).
- **Scopes:** *Code (Read)*, *Build (Read)* and *Project and Team (Read)*. These cover pipelines, runs, timelines, repos and policies.
- **PAT expiry:** a PAT can't read its own expiry date, so store it as `PatExpiresOn` next to the PAT. The top bar warns within 7 days, and a 401 shows "PAT expired or revoked".
- Token access sits behind `IAdoTokenProvider`, so switching to Entra ID later would change only that class.

### Read-only by design
- The PAT has only *(Read)* scopes, so ADO itself rejects any write.
- `AdoHttpHandler` rejects any request that isn't `GET`.
- Client interfaces expose only query methods.
- Kestrel listens on `localhost` only.

---

## 3. Solution structure

```
devops-blazor/
├─ DevOpsBlazor.sln
├─ src/
│  ├─ DevOpsBlazor.Web/                 # Blazor Web App (UI only)
│  │  ├─ Components/
│  │  │  ├─ Layout/                     # MainLayout, NavMenu, project picker, PAT-expiry banner
│  │  │  └─ Shared/                     # StatusBadge, ProblemLabel, Duration, RunStrip, OpenInAdo, Loading/Error
│  │  ├─ Features/
│  │  │  ├─ Pipelines/                  # PipelineList, PipelineRuns, RunDetail
│  │  │  └─ Compliance/                 # PolicyMatrix, RepoPolicies, ComplianceSummary
│  │  └─ Program.cs
│  ├─ DevOpsBlazor.AzureDevOps/         # ADO client library (no UI)
│  │  ├─ Auth/                          # IAdoTokenProvider, PatTokenProvider
│  │  ├─ Http/                          # AdoHttpHandler (auth, api-version, GET-only guard, error mapping), paging helper
│  │  ├─ Links/                         # AdoLinks: every portal URL is built here
│  │  ├─ Clients/                       # IProjectClient, IBuildClient, IGitClient, IPolicyClient
│  │  └─ Models/
│  └─ DevOpsBlazor.Core/                # logic with no I/O
│     ├─ RunAnalysis/                   # duration stats, problem-label detectors
│     └─ Compliance/                    # effective-policy resolver, rules, evaluator
└─ tests/
   ├─ DevOpsBlazor.AzureDevOps.Tests/
   ├─ DevOpsBlazor.Core.Tests/          # most of the value: labels and rules against fixture data
   └─ DevOpsBlazor.Web.Tests/
```

**Configuration** (`appsettings.json`; the PAT goes in user-secrets):
```json
"AzureDevOps": {
  "Organization": "https://dev.azure.com/contoso",
  "Projects": [ "Platform", "Payments" ],          // empty = all projects
  "RunsPerPipeline": 20,
  "CacheMinutes": { "Pipelines": 10, "Runs": 1, "Policies": 10 }
},
"RunLabels": {
  "SlowFactor": 1.5,              // duration > 1.5 × median of recent successful runs
  "LongQueueMinutes": 10,
  "FlakyFlipsThreshold": 3,       // pass↔fail flips on the same branch within the window
  "StaleDays": 30
},
"Compliance": { "BaselineFile": "compliance-baseline.json" }
```

**Cross-cutting pieces to build first**
- `AdoHttpHandler`: adds the auth header and `api-version=7.1`, enforces GET-only, and maps 401/403/404/429 to typed exceptions with actionable messages ("PAT lacks *Build (Read)*").
- `PagedAsync<T>()`: follows `x-ms-continuationtoken` and returns `IAsyncEnumerable<T>`.
- `AdoLinks`: one place that builds every portal URL. Portal URLs aren't a formal API, so if one changes there's one place to fix.
- Shared components: `StatusBadge`, `ProblemLabel` (a chip with a tooltip that explains *why*), `RunStrip` (a row of coloured dots for the last N runs), `Duration`, and `OpenInAdo`.

---

## 4. Feature roadmap

### Phase 0: Skeleton (½–1 day)
- `dotnet new blazor --interactivity Server`, add Fluent UI, and create the projects above.
- PAT via user-secrets. A health page calls `GET _apis/connectionData` to show *who am I / is the token OK*, and checks each needed scope.
- Project picker (`GET _apis/projects`), filtered by `Projects` in config.
- CI: a GitHub Actions build-and-test workflow.

### Phase 1: Pipelines and recent runs
**Pipeline list** (per project, or all configured projects):

| Column | Source |
|---|---|
| Name, folder, type (YAML / classic) | `GET {project}/_apis/build/definitions?includeLatestBuilds=true` |
| **Run strip**: coloured dots for the last N runs on the default branch (hover shows result, duration and date; click opens the run) | `GET {project}/_apis/build/builds?definitions={ids}&maxBuildsPerDefinition={N}&queryOrder=finishTimeDescending`. This is **one call for many pipelines**, not one per pipeline. |
| Last result, last run time, pass rate over N | computed |
| Median duration plus a sparkline | computed |
| Problem labels (Phase 2) | computed |
| Open in ADO | `AdoLinks.Pipeline(...)` |

Filter by folder, name, result and "has problems". Sort by any column.

**Pipeline runs page** (click a pipeline): the last N runs (10/25/50) with:
- build number, result, status, branch, trigger reason, who started it, commit,
- **queue wait** (`startTime − queueTime`) and **duration** (`finishTime − startTime`),
- a duration trend chart with successful and failed runs coloured differently, plus a median line,
- a branch filter (default branch / all / a specific branch),
- **Open in ADO**, **Open logs** and **Open first failure** per run (see below).

### Phase 2: Problem labels and run detail
**Problem labels** are computed in `DevOpsBlazor.Core.RunAnalysis` from data already fetched. Each label has a tooltip that explains the evidence (e.g. "Failed on the last 4 runs of `main`, since 12 Oct, after commit a1b2c3").

| Label | Applies to | Rule (thresholds configurable) |
|---|---|---|
| 🔴 **Broken** | pipeline | The latest run on the default branch failed. Shows the streak length and "failing since". |
| 🟠 **Flaky** | pipeline | Results flip between pass and fail at least `FlakyFlipsThreshold` times on the same branch in the window, especially reruns of the same commit that passed. |
| 🟡 **Slow** | run | Duration is more than `SlowFactor` × the median of recent successful runs. |
| 🟡 **Getting slower** | pipeline | The median duration of the latest third of runs is noticeably higher than the earliest third. |
| 🟡 **Long queue** | run | Queue wait is more than `LongQueueMinutes`, a hint about agent capacity. |
| 🟠 **Partially succeeded** | run | Result is `partiallySucceeded`, i.e. a step with `continueOnError` failed and was hidden. |
| ⚪ **Timed out / cancelled** | run | Result is cancelled, or a timeline issue mentions a timeout. |
| 🔁 **Retried** | run | A timeline record has `attempt > 1` (stages or jobs were rerun). |
| ⚠️ **Warnings rising** | pipeline | The timeline warning count is trending up. |
| 💤 **Stale** | pipeline | No runs in `StaleDays` days. It may be dead or have a disabled trigger. |

Labels that need the timeline (Retried, Warnings rising, Timed out) fetch it lazily, for the runs on screen only.

**Run detail page** (all small JSON calls, no log content):

| Feature | Source |
|---|---|
| Stages → jobs → tasks tree with result, duration and error/warning counts, with failed nodes expanded | `GET .../build/builds/{id}/timeline` |
| **Error summary**: the error messages from `record.issues`, which often show what broke without opening the log | timeline |
| **Open log** per task | `https://dev.azure.com/{org}/{project}/_build/results?buildId={id}&view=logs&j={jobId}&t={taskId}` |
| **Open first failure** | the same link, aimed at the first failed task |
| **Download logs (zip)** | a plain browser link to `.../_apis/build/builds/{id}/logs?$format=zip`, downloaded directly from ADO with your browser's sign-in |
| Commits in the run | `GET .../build/builds/{id}/changes` |
| Copy-link buttons | `AdoLinks` |

### Phase 3: Repo policy viewer
**Data** (per project, kept to a few calls):

| Data | REST endpoint |
|---|---|
| Repos (name, default branch, disabled flag) | `GET {project}/_apis/git/repositories` |
| **All policy configurations in the project, in one call** | `GET {project}/_apis/policy/configurations` |
| Policy type names | `GET {project}/_apis/policy/types` |
| Cross-check of the effective policies for one repo and branch | `GET {project}/_apis/git/policy/configurations?repositoryId={id}&refName=refs/heads/{branch}` |

**Effective-policy resolver** (`Core.Compliance`). A policy's `settings.scope` can target:
- a specific repo and branch,
- a branch prefix (`matchKind: Prefix`, e.g. `refs/heads/release/`),
- every repo in the project (`repositoryId: null`),
- the repo itself with no branch (repository-level policies such as file size limits, path length, reserved names and commit author email).

The resolver works out which policies **actually apply** to each repo's default branch (and any other branches you care about, e.g. `release/*`), along with **where each one comes from** (project-wide vs repo-specific). Policies that are disabled (`isEnabled: false`) or not blocking (`isBlocking: false`, i.e. optional) are kept but flagged, because "configured but optional" is a common compliance gap.

**Per-repo policy page:** a readable card for each effective policy:
- **Minimum reviewers:** count, whether the author's own approval counts (`creatorVoteCounts`), whether votes reset on new pushes (`resetOnSourcePush`), and whether "approve with suggestions" or "waiting for author" block the merge.
- **Build validation:** which pipeline, blocking or optional, expiry, path filters.
- **Required reviewers:** who, and for which paths.
- **Comment resolution**, **linked work items** and **merge strategy** (which merge types are allowed).
- **Repository-level policies** (file size, path length, reserved names, author email).

Each card links to the repo's policy settings in ADO.

### Phase 4: Compliance baseline and visualisation
**Baseline as data.** `compliance-baseline.json` lists the rules your repos should meet. Each rule has an id, a severity (High / Medium / Low), parameters, and the branches it applies to:
```json
{
  "branches": [ "default", "release/*" ],
  "rules": [
    { "id": "MinReviewers",           "severity": "High",   "min": 2 },
    { "id": "NoSelfApproval",         "severity": "High" },
    { "id": "ResetVotesOnPush",       "severity": "Medium" },
    { "id": "BuildValidationBlocking","severity": "High" },
    { "id": "CommentResolution",      "severity": "Medium" },
    { "id": "LinkedWorkItems",        "severity": "Low" },
    { "id": "AllowedMergeTypes",      "severity": "Low",    "allowed": [ "squash" ] },
    { "id": "DefaultBranchIsMain",    "severity": "Low" },
    { "id": "PoliciesAreBlocking",    "severity": "High" }
  ],
  "exemptions": [
    { "repo": "Platform/sandbox", "rules": [ "*" ], "reason": "Personal playground", "until": "2027-01-01" }
  ]
}
```
Each rule is a small `IComplianceRule` class that takes a repo's effective policies and returns **Pass / Fail / Warn / Exempt / N/A**, plus a human-readable reason ("Min reviewers is 1, baseline requires 2", "Build validation exists but is optional"). New rules are one class and one test.

**Visualisations**
1. **Compliance matrix (main view):** repos as rows and rules as columns. Cells are coloured by outcome (green pass, red fail, amber warn, grey exempt or N/A). Hovering a cell shows the reason, and clicking it opens the per-repo page. You can sort by "most failures", filter by project, severity or outcome, and hide disabled repos.
2. **Summary header:** % of repos fully compliant, a bar of failures per rule (which rule is broken most often), and a per-project breakdown.
3. **Per-repo scorecard:** the repo's rule results plus its effective policies (from Phase 3), with "Fix in ADO" links to the right settings page.
4. **Policy source view:** shows whether a repo is covered by project-wide policies or repo-specific ones. This matters when deciding whether to fix a gap once at project level or repo by repo.

**Export:** the whole matrix to CSV, and a Markdown or HTML report (summary plus failures with reasons) to attach to tickets or audits. The report is stamped with the date and the baseline version.

**Later, optional:** save a compliance snapshot each time you run it (a local JSON or SQLite file), so you can see compliance improve over time and diff "what changed since last month".

---

## 5. Design notes and gotchas

- **Throttling:** ADO rate-limits per user. Prefer **one call per project** (all builds via `maxBuildsPerDefinition`, all policies via `policy/configurations`) over one call per pipeline or repo, cap concurrency at about 4, and honour `Retry-After`.
- **Paging:** always go through the paging helper. `policy/configurations` and `builds` both page.
- **Durations:** use UTC throughout and convert to local time only in the UI. A run still in progress has no `finishTime`; show it as "running for X" and leave it out of medians.
- **Label quality:** labels are hints, not verdicts. Each tooltip shows its evidence, and each threshold is configurable, so they can be tuned once real data shows what's noisy.
- **Policy scope matching:** project-wide and prefix-scoped policies are where compliance tools usually get it wrong. The resolver gets the most tests, with fixtures covering each scope shape, and the `git/policy/configurations` endpoint is used to cross-check it.
- **Deep links:** everything shows an "Open in ADO" link, so the app adds to the portal rather than replacing it.
- **Local security:** listens on localhost only, never logs the token, and keeps the PAT in user-secrets only.

---

## 6. Later (parked)

- **Agent pools:** wait and run times per pool and agent, with build/test/release roles and Microsoft-hosted vs self-hosted handling. Data comes from `distributedtask/pools`, `/agents` and `/jobrequests` (the last is undocumented), with history kept in SQLite by a background collector. The "Long queue" label in Phase 2 is a lightweight first signal for this.
- **"What needs my attention?" dashboard:** my PRs, PRs awaiting my review, pending approvals (read-only, linking to ADO), and a board of every currently failing pipeline.
- **Error search:** search timeline error messages across recent runs ("when did this error first appear?").
- **Compare two runs:** commits, parameters and failed tasks for a good run vs a bad one.
- **Stale branch report**, **branch permissions** compliance (Security API), **flaky test detector** (Test API), **release notes generator**.
- **Multi-org support:** the config shape can be extended to a list.

---

## 7. Suggested order of PRs

1. Solution skeleton, Fluent UI layout, CI workflow.
2. `DevOpsBlazor.AzureDevOps`: auth, GET-only guard, error mapping, paging, `AdoLinks`, and tests.
3. Health page and project picker.
4. Pipeline list with run strip, last result and median duration.
5. Pipeline runs page: durations, queue wait, trend chart, deep links.
6. Problem labels (non-timeline ones first), then the run detail page with timeline, error summary and "open first failure".
7. Policy client and effective-policy resolver, with thorough tests.
8. Per-repo policy page.
9. Compliance baseline, rules and matrix view.
10. Summary charts and CSV/Markdown export.

---

## 8. Decisions and open questions

**Decided**
- Runs locally only. A PAT with read-only scopes, kept in user-secrets.
- Read-only: no actions against ADO.
- Logs and other details are opened in the ADO portal through deep links.
- MVP focus: pipelines and runs (status, duration, problem labels), plus repo policy compliance.
- Agent analytics are parked.

**Open**
- One ADO organisation? Roughly how many projects, repos and pipelines? Scale affects caching and paging choices.
- What's your compliance baseline? The rules in Phase 4 are a starting guess. Which matter, and at what thresholds (e.g. 1 or 2 reviewers, self-approval allowed)?
- Which branches must be protected: just the default branch, or also `release/*` or others?
- Do your repos mostly rely on **project-wide** policies or **per-repo** ones?
