# DevOps Blazor: Plan

A personal Blazor app that sits alongside Azure DevOps (ADO). It answers the questions the ADO portal makes slow to answer: *what's broken, why, and what's waiting on me?* It's built in small, shippable slices so new features are cheap to add.

---

## 1. Goals and non-goals

**Goals**
- Fast, read-mostly views over repos, pipelines, runs, logs and policies.
- Views that span projects (and possibly organisations), which the portal does poorly.
- An architecture where adding a feature means one API client method, one page and maybe one component.

**Non-goals (for now)**
- Replacing the ADO portal for editing (YAML editing, work item boards and so on).
- Multi-tenant SaaS hosting. The app is single user and runs locally or in a private App Service.

---

## 2. Technology choices

| Concern | Choice | Why |
|---|---|---|
| Framework | **.NET 10 (LTS), Blazor Web App** with **Interactive Server** render mode | Tokens and PATs stay on the server and are never shipped to the browser. Server-side HttpClient calls avoid CORS. Live updates come over SignalR for free. |
| UI kit | **Microsoft Fluent UI Blazor** (`Microsoft.FluentUI.AspNetCore.Components`) | Looks native next to ADO. Includes a good `FluentDataGrid` with virtualisation, badges, a tree view and dialogs. (MudBlazor is a fine alternative.) |
| ADO access | **Typed `HttpClient` against the ADO REST API (v7.1)**, behind our own interfaces | Lightweight and easy to mock. It gives full control over paging and continuation tokens, `$top`, and log streaming. The official `Microsoft.TeamFoundationServer.Client` SDK works but is heavy and lags the API. We can drop to it for a specific area if that ever helps. |
| Resilience | `Microsoft.Extensions.Http.Resilience` (Polly) | Retries and backoff, and honours `Retry-After` on ADO 429/503 throttling. |
| Caching | `HybridCache` (in-memory to start) | Repo and pipeline lists change rarely. Cache them for minutes; keep runs short-lived. |
| Charts | ApexCharts.Blazor (or Fluent charts) | Run duration and pass-rate trends. |
| Tests | xUnit + bUnit + `RichardSzalay.MockHttp` | Unit-test API clients against recorded JSON, and test components in isolation. |

### Authentication (staged)
1. **Phase 1: Personal Access Token (PAT).** Store it in `dotnet user-secrets` locally (and Key Vault if deployed). Use scopes *Code (Read)*, *Build (Read)* and *Project and Team (Read)*, and add more as features need them. Send it as Basic auth (`:{PAT}` base64).
2. **Phase 2: Microsoft Entra ID.** Use `DefaultAzureCredential` / MSAL for the ADO resource (`499b84ac-1321-427f-aa17-267ca6975798/.default`). This removes PAT expiry pain, and ADO is moving away from PATs anyway. Hide it behind an `IAdoTokenProvider` interface so the swap touches only that class.

---

## 3. Solution structure

```
devops-blazor/
├─ DevOpsBlazor.sln
├─ src/
│  ├─ DevOpsBlazor.Web/                 # Blazor Web App (UI only)
│  │  ├─ Components/
│  │  │  ├─ Layout/                     # MainLayout, NavMenu, org/project picker
│  │  │  └─ Shared/                     # StatusBadge, RelativeTime, LogViewer, Loading/Error states
│  │  ├─ Features/                      # one folder per feature (vertical slices)
│  │  │  ├─ Repositories/               # RepoList.razor, RepoDetail.razor
│  │  │  ├─ Pipelines/                  # PipelineList.razor, PipelineRuns.razor, RunDetail.razor
│  │  │  ├─ Policies/
│  │  │  └─ Dashboard/
│  │  └─ Program.cs
│  ├─ DevOpsBlazor.AzureDevOps/         # ADO client library (no UI dependencies)
│  │  ├─ Auth/                          # IAdoTokenProvider, PatTokenProvider, EntraTokenProvider
│  │  ├─ Http/                          # AdoHttpHandler (auth, api-version, paging helper)
│  │  ├─ Clients/                       # IGitClient, IPipelineClient, IBuildClient, IPolicyClient, ...
│  │  ├─ Models/                        # DTO records mapped from REST JSON
│  │  └─ ServiceCollectionExtensions.cs # AddAzureDevOps(config)
│  └─ DevOpsBlazor.Core/ (optional, later) # cross-cutting logic: flaky detection, policy audit rules
└─ tests/
   ├─ DevOpsBlazor.AzureDevOps.Tests/   # client tests against recorded JSON fixtures
   └─ DevOpsBlazor.Web.Tests/           # bUnit component tests
```

**Configuration** (`appsettings.json` with secrets elsewhere):
```json
"AzureDevOps": {
  "Organizations": [
    { "Name": "contoso", "Url": "https://dev.azure.com/contoso", "DefaultProject": "Platform" }
  ],
  "AuthMode": "Pat",
  "CacheMinutes": { "Repos": 10, "Pipelines": 10, "Runs": 1 }
}
```

**Key cross-cutting pieces to build first**
- `AdoHttpHandler`: adds the auth header and `api-version=7.1`, logs the request, and maps failures (401 → "PAT expired or invalid", 403 → "missing scope X", 404 and 429) to typed exceptions the UI can show nicely.
- `PagedAsync<T>()` helper: follows the `x-ms-continuationtoken` header and `continuationToken` query parameter, and returns `IAsyncEnumerable<T>`.
- Org/project context: a scoped `AdoContext` service plus a picker in the top bar, kept in the URL (`/contoso/Platform/pipelines`) so links can be shared and bookmarked.
- Shared components for `StatusBadge` (succeeded, failed, partiallySucceeded, canceled, inProgress), relative time, empty, loading and error states, and deep links back to the ADO portal.

---

## 4. Feature roadmap

Each phase is independently useful and ends with something runnable.

### Phase 0: Skeleton (½–1 day)
- `dotnet new blazor --interactivity Server`, add Fluent UI and create the solution and projects above.
- PAT via user-secrets, `AddAzureDevOps()`, and a health page that calls `GET _apis/connectionData` to show *who am I / is my token OK*.
- Project list (`GET {org}/_apis/projects`) and the org/project picker.
- CI: a GitHub Actions workflow that builds and tests on push.

### Phase 1: Repositories and pipelines (your core asks)
| Feature | REST endpoint(s) |
|---|---|
| **List repositories** with default branch, size and last update, plus a filter box | `GET {project}/_apis/git/repositories` |
| Repo detail: branches with ahead/behind counts against the default branch | `GET .../git/repositories/{id}/stats/branches` |
| **List pipelines** with folder tree, filter, and the last run's result inline | `GET {project}/_apis/pipelines`, or `GET .../build/definitions?includeLatestBuilds=true` (gives the last result in one call) |
| **Click a pipeline → last N runs** (N selectable: 10/25/50) with result, status, branch, trigger reason, who started it, duration and commit | `GET .../build/builds?definitions={id}&$top={N}&queryOrder=queueTimeDescending` |
| **Pass/fail at a glance**: colour-coded badges plus a pass-rate % and duration sparkline for the last N runs | computed from the runs above |

> I'd use the **Build API** (`_apis/build/builds`) rather than the newer Pipelines Runs API for run lists. It supports `$top`, branch and result filters, and returns richer data (reason, requestedFor, sourceVersion).

### Phase 2: Run detail and logs
| Feature | REST endpoint(s) |
|---|---|
| **Run timeline**: stages → jobs → tasks tree with result, duration and error/warning counts, with failed nodes auto-expanded | `GET .../build/builds/{buildId}/timeline` |
| **View a log** for a task inline (monospace viewer, virtualised, ANSI/`##[error]` highlighting, jump-to-first-error) | `GET .../build/builds/{buildId}/logs/{logId}` (`logId` comes from the timeline record) |
| **Download all logs** as a zip | `GET .../build/builds/{buildId}/logs?$format=zip`, streamed to the browser via JS interop |
| Error summary: the issues from the timeline (`record.issues`) listed at the top of the run page | timeline |
| Artifacts list and download | `GET .../build/builds/{buildId}/artifacts` |
| Changes in this run (commits) | `GET .../build/builds/{buildId}/changes` |

### Phase 3: Repo policies
| Feature | REST endpoint(s) |
|---|---|
| **Policies on a repo/branch**: min reviewers, required reviewers, build validation, comment resolution, linked work items, merge strategy | `GET {project}/_apis/policy/configurations?repositoryId={id}&refName=refs/heads/main` |
| Friendly names and settings rendering for each policy type | `GET {project}/_apis/policy/types` |
| **Policy audit matrix** (high value): every repo as a row, each policy as a column, highlighting repos whose default branch has *no* PR policy, too few reviewers or no build validation | the same calls, fanned out across repos |
| Branch security / permissions view (later; the API is more complex) | Security namespaces API |

### Phase 4: "What needs me?" dashboard
- **My PRs and PRs awaiting my review** across all projects: `GET _apis/git/pullrequests?searchCriteria.reviewerId={me}&searchCriteria.status=active`.
- **Pending pipeline approvals for me** (environment checks): `GET {project}/_apis/pipelines/approvals?state=pending`, with approve/reject in place (needs a write scope).
- **Red board**: every pipeline whose latest run on the default branch failed, across projects. This is the "what's broken right now" screen.
- **Watchlist**: pin favourite pipelines and repos, stored locally (a SQLite or JSON file).

### Phase 5: Actions (write operations, behind confirmation dialogs)
- Queue a run with branch and parameters: `POST .../pipelines/{id}/runs`.
- Retry failed stages: `PATCH .../build/builds/{id}/stages/{stage}`.
- Cancel a run.
- These need broader PAT scopes (*Build: Read & execute*), so we add them deliberately.

### Further ideas (backlog, roughly by value)
1. **Flaky test detector**: use the Test Runs/Results APIs to find tests that flip between pass and fail on the same commit or branch.
2. **Pipeline trends**: duration and pass rate over 30/90 days, and queue-time vs run-time (shows agent pool saturation).
3. **Log search**: grep across the failed-task logs of the last N runs ("when did this error first appear?").
4. **Compare two runs**: diff of commits, parameters, variables and failed tasks between a good run and a bad one.
5. **Agent pools and agents**: online/offline status, current jobs and capabilities (`_apis/distributedtask/pools`, `/agents?includeCapabilities=true`).
6. **Service connections and variable groups inventory**: who uses what, plus a search for which pipelines reference a given variable group (`serviceendpoint/endpoints`, `distributedtask/variablegroups`).
7. **Stale branch report**: branches with no commits in X days, or already merged into main.
8. **Release notes generator**: work items and commits between two runs or tags, exported as Markdown.
9. **Cross-repo code search** (`almsearch.dev.azure.com/.../_apis/search/codesearchresults`).
10. **YAML viewer**: show the pipeline's YAML (`GET .../pipelines/{id}?$expand=...`, or fetch the file from the repo) with template references linked.
11. **Notifications**: a background `IHostedService` polls the watchlist and pushes toast notifications (via the existing SignalR circuit) when a watched pipeline fails.
12. **AI failure summary** (optional): send the failing task's log tail to an LLM for a "likely cause" explanation.
13. **Multi-org support**: the config already allows it; the picker switches the org.

---

## 5. Design notes and gotchas

- **Throttling:** ADO rate-limits per user (TSTUs). Fan-out views (policy audit, red board) should limit concurrency (`Parallel.ForEachAsync` with `MaxDegreeOfParallelism ≈ 4–8`), cache results and honour `Retry-After`.
- **Paging:** list endpoints cap results (often 100–1000) and return a continuation token. Always go through the paging helper.
- **Large logs:** stream them (`ReadAsStreamAsync`) and render with `Virtualize`. Never load a 50 MB log into a single string in the circuit.
- **Blazor Server circuits:** keep per-user state in scoped services, and dispose of `CancellationTokenSource`s when components are disposed so abandoned page loads stop calling ADO.
- **Errors:** surface 401/403 with an actionable message ("Your PAT lacks the *Build (Read)* scope").
- **Deep links:** every entity shows an "Open in Azure DevOps" link, so the app augments the portal rather than competing with it.
- **Security:** never log tokens, keep the PAT out of `appsettings.json`, and if hosted, put it behind Entra ID auth (`Microsoft.Identity.Web`).

---

## 6. Suggested order of first PRs

1. Solution skeleton, Fluent UI layout, CI workflow.
2. `DevOpsBlazor.AzureDevOps`: auth handler, error mapping, paging helper and tests.
3. Health page and project picker.
4. Repository list.
5. Pipeline list → pipeline runs page (last N, pass/fail, sparkline).
6. Run detail: timeline tree and log viewer and zip download.
7. Repo policies page, then the policy audit matrix.
8. Dashboard: my PRs, approvals, red board.

---

## 7. Open questions

- One ADO organisation or several?
- Local-only (`dotnet run`) or hosted (Azure App Service with Entra login)? This decides when Phase 2 auth is needed.
- Is a PAT acceptable to start with, or is there an org policy restricting PATs?
- Which backlog items matter most to you? They decide what follows Phase 3.
