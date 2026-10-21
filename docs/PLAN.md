# DevOps Blazor: Plan

A personal, **read-only** Blazor app that sits alongside Azure DevOps (ADO). It answers the questions the ADO portal makes slow to answer: *what's broken, why, what's waiting on me, and are our agents keeping up?* It's built in small, shippable slices so new features are cheap to add.

---

## 1. Goals and non-goals

**Goals**
- Fast, read-only views over repos, pipelines, runs, policies and agent pools.
- One-click routes to the exact log in the ADO portal, rather than viewing logs in the app.
- **Agent pool insight:** how long jobs wait for an agent, how long they run, and how busy each pool and agent is.
- Views that span projects (and possibly organisations), which the portal does poorly.
- An architecture where adding a feature means one API client method, one page and maybe one component.

**Non-goals**
- **Any write operation against ADO.** No queuing, retrying or cancelling runs, no approving, no editing. The only thing the app writes to is its own local database (see §4, Phase 2).
- Replacing the ADO portal.
- Multi-tenant SaaS hosting. The app is single user and runs locally or in a private App Service.

---

## 2. Technology choices

| Concern | Choice | Why |
|---|---|---|
| Framework | **.NET 10 (LTS), Blazor Web App** with **Interactive Server** render mode | Tokens and PATs stay on the server and are never shipped to the browser. Server-side HttpClient calls avoid CORS. Live updates come over SignalR for free. |
| UI kit | **Microsoft Fluent UI Blazor** (`Microsoft.FluentUI.AspNetCore.Components`) | Looks native next to ADO. Includes a good `FluentDataGrid` with virtualisation, badges, a tree view and dialogs. (MudBlazor is a fine alternative.) |
| ADO access | **Typed `HttpClient` against the ADO REST API (v7.1)**, behind our own interfaces | Lightweight and easy to mock. It gives full control over paging and continuation tokens and `$top`. The official `Microsoft.TeamFoundationServer.Client` SDK works but is heavy and lags the API. |
| Resilience | `Microsoft.Extensions.Http.Resilience` (Polly) | Retries and backoff, and honours `Retry-After` on ADO 429/503 throttling. |
| Caching | `HybridCache` (in-memory to start) | Repo and pipeline lists change rarely. Cache them for minutes; keep runs and agent status short-lived. |
| Local store | **SQLite via EF Core** | Keeps a history of agent job data beyond what ADO retains, so wait and run time trends can cover weeks or months. |
| Charts | ApexCharts.Blazor | Run duration and pass-rate trends, wait-time percentiles, pool heatmaps. |
| Tests | xUnit + bUnit + `RichardSzalay.MockHttp` | Unit-test API clients against recorded JSON, and test components in isolation. |

### Authentication (staged)
1. **Phase 1: Personal Access Token (PAT), read scopes only.** Store it in `dotnet user-secrets` locally (and Key Vault if deployed). Use scopes *Code (Read)*, *Build (Read)*, *Project and Team (Read)* and *Agent Pools (Read)*, plus *Test Management (Read)* if the flaky test feature is built. Send it as Basic auth (`:{PAT}` base64).
2. **Phase 2: Microsoft Entra ID.** Use `DefaultAzureCredential` / MSAL for the ADO resource (`499b84ac-1321-427f-aa17-267ca6975798/.default`). This removes PAT expiry pain, and ADO is moving away from PATs anyway. Hide it behind an `IAdoTokenProvider` interface so the swap touches only that class.

### Read-only by design
Read-only is enforced in three places, not just by convention:
- The PAT has only *(Read)* scopes, so ADO itself rejects any write.
- `AdoHttpHandler` rejects any request that isn't `GET`, apart from an explicit allow-list of read-only `POST` queries (WIQL, code search).
- Client interfaces expose only query methods.

---

## 3. Solution structure

```
devops-blazor/
├─ DevOpsBlazor.sln
├─ src/
│  ├─ DevOpsBlazor.Web/                 # Blazor Web App (UI only)
│  │  ├─ Components/
│  │  │  ├─ Layout/                     # MainLayout, NavMenu, org/project picker
│  │  │  └─ Shared/                     # StatusBadge, RelativeTime, Duration, OpenInAdo link, Loading/Error states
│  │  ├─ Features/                      # one folder per feature (vertical slices)
│  │  │  ├─ Repositories/               # RepoList.razor, RepoDetail.razor
│  │  │  ├─ Pipelines/                  # PipelineList.razor, PipelineRuns.razor, RunDetail.razor
│  │  │  ├─ Agents/                     # PoolList.razor, PoolDetail.razor, AgentDetail.razor
│  │  │  ├─ Policies/
│  │  │  └─ Dashboard/
│  │  └─ Program.cs
│  ├─ DevOpsBlazor.AzureDevOps/         # ADO client library (no UI dependencies)
│  │  ├─ Auth/                          # IAdoTokenProvider, PatTokenProvider, EntraTokenProvider
│  │  ├─ Http/                          # AdoHttpHandler (auth, api-version, read-only guard, paging helper)
│  │  ├─ Links/                         # AdoLinks: builds portal URLs (run, job/task log, logs zip, repo, pool)
│  │  ├─ Clients/                       # IGitClient, IBuildClient, IPolicyClient, IAgentPoolClient, ...
│  │  ├─ Models/                        # DTO records mapped from REST JSON
│  │  └─ ServiceCollectionExtensions.cs # AddAzureDevOps(config)
│  ├─ DevOpsBlazor.Core/                # logic with no I/O: wait/run time stats, percentiles, policy audit rules
│  └─ DevOpsBlazor.Data/                # EF Core + SQLite: job request history, watchlist
│     └─ Collectors/                    # AgentJobCollector (IHostedService that polls and stores job history)
└─ tests/
   ├─ DevOpsBlazor.AzureDevOps.Tests/   # client tests against recorded JSON fixtures
   ├─ DevOpsBlazor.Core.Tests/          # stats and rules tests
   └─ DevOpsBlazor.Web.Tests/           # bUnit component tests
```

**Configuration** (`appsettings.json` with secrets elsewhere):
```json
"AzureDevOps": {
  "Organizations": [
    { "Name": "contoso", "Url": "https://dev.azure.com/contoso", "DefaultProject": "Platform" }
  ],
  "AuthMode": "Pat",
  "CacheMinutes": { "Repos": 10, "Pipelines": 10, "Runs": 1, "Agents": 1 },
  "AgentJobCollector": { "Enabled": true, "PollMinutes": 15, "RetentionDays": 180 }
}
```

**Key cross-cutting pieces to build first**
- `AdoHttpHandler`: adds the auth header and `api-version=7.1`, enforces the read-only guard, logs the request, and maps failures (401 → "PAT expired or invalid", 403 → "missing scope X", 404 and 429) to typed exceptions the UI can show nicely.
- `PagedAsync<T>()` helper: follows the `x-ms-continuationtoken` header and `continuationToken` query parameter, and returns `IAsyncEnumerable<T>`.
- Org/project context: a scoped `AdoContext` service plus a picker in the top bar, kept in the URL (`/contoso/Platform/pipelines`) so links can be shared and bookmarked.
- Shared components for `StatusBadge` (succeeded, failed, partiallySucceeded, canceled, inProgress), durations, relative time, empty, loading and error states, and deep links back to the ADO portal (via `AdoLinks`).

---

## 4. Feature roadmap

Each phase is independently useful and ends with something runnable.

### Phase 0: Skeleton (½–1 day)
- `dotnet new blazor --interactivity Server`, add Fluent UI and create the solution and projects above.
- PAT via user-secrets, `AddAzureDevOps()`, and a health page that calls `GET _apis/connectionData` to show *who am I / is my token OK*. It also checks which of the needed scopes work.
- Project list (`GET {org}/_apis/projects`) and the org/project picker.
- CI: a GitHub Actions workflow that builds and tests on push.

### Phase 1: Repositories and pipelines
| Feature | REST endpoint(s) |
|---|---|
| **List repositories** with default branch, size and last update, plus a filter box | `GET {project}/_apis/git/repositories` |
| Repo detail: branches with ahead/behind counts against the default branch | `GET .../git/repositories/{id}/stats/branches` |
| **List pipelines** with folder tree, filter, and the last run's result inline | `GET .../build/definitions?includeLatestBuilds=true` (gives the last result in one call) |
| **Click a pipeline → last N runs** (N selectable: 10/25/50) with result, status, branch, trigger reason, who started it, queue wait, duration and commit | `GET .../build/builds?definitions={id}&$top={N}&queryOrder=queueTimeDescending` |
| **Pass/fail at a glance**: colour-coded badges plus a pass-rate % and duration sparkline for the last N runs | computed from the runs above |

> I'd use the **Build API** (`_apis/build/builds`) rather than the newer Pipelines Runs API for run lists. It supports `$top`, branch and result filters, and returns richer data (reason, requestedFor, sourceVersion, and `queueTime`/`startTime`/`finishTime`, which give a first measure of wait time per run).

### Phase 2: Agent pools, wait times and run times ⭐
This is the priority after the core lists. Pools are organisation-level, so these pages sit outside the project picker.

**Data sources**
| Data | REST endpoint | Notes |
|---|---|---|
| Pools (self-hosted and Microsoft-hosted), size, type | `GET {org}/_apis/distributedtask/pools` | |
| Agents in a pool: online/offline, enabled, version, OS, capabilities, current job, last completed job | `GET {org}/_apis/distributedtask/pools/{poolId}/agents?includeCapabilities=true&includeAssignedRequest=true&includeLastCompletedRequest=true` | |
| **Job history for a pool** (the portal's pool *Jobs* tab) | `GET {org}/_apis/distributedtask/pools/{poolId}/jobrequests` | Each request has `queueTime`, `assignTime`, `receiveTime`, `finishTime`, `result`, the agent that ran it, and the pipeline/definition that owns it. This is the main source for wait and run times. It's **not formally documented**, so it's wrapped in `IAgentPoolClient` with fixture tests to catch shape changes. |
| Fallback / per-job detail | build timeline (`.../build/builds/{id}/timeline`): job records have `startTime`, `finishTime`, `workerName` | Used if `jobrequests` misbehaves, and to link a job back to its stage and run. |
| Long-range trends (optional, later) | Analytics OData (`analytics.dev.azure.com/{org}/_odata/...`, e.g. `TaskAgentRequestSnapshots`, `PipelineRuns`) | Powers ADO's own *pool consumption* report. Worth trying if Analytics is enabled in your org; parts of it are preview. |

**Metric definitions** (computed in `DevOpsBlazor.Core`)
- **Wait time** = `assignTime − queueTime`: how long the job waited for a free agent.
- **Run time** = `finishTime − assignTime`, or `finishTime − receiveTime` when present.
- **Utilisation** (per agent, per pool) = busy time ÷ online time over the window.
- **Concurrency** = jobs running at once over time, compared with pool size (online agents).
- Report p50 / p90 / p95 / max, not just averages, because waits are spiky.

**Pages**
1. **Pools overview:** one card per pool showing agents online/total, jobs running and queued right now, and p50/p90 wait and run time over the last 24h and 7d, with a red flag when p90 wait exceeds a threshold you set.
2. **Pool detail:**
   - Wait-time and run-time trend charts (daily percentiles).
   - **Heatmap of wait time by day of week × hour**, which shows when the pool is under-provisioned.
   - Concurrency vs capacity chart: running and queued jobs over time against online agent count.
   - **Top consumers:** pipelines ranked by total agent minutes and by total wait caused.
   - Job list with filters (pipeline, result, agent, date range) and sorting by wait or run time.
3. **Agent detail:** status, version, key capabilities, jobs run, utilisation %, average run time, and **failure rate compared with the pool average**. An agent that fails far more often than its siblings is usually a broken machine, not broken pipelines.
4. **Agent health warnings:** offline or disabled agents, agents on outdated versions, agents that have been idle for days, and agents with an unusually high failure rate.
5. **Per-pipeline view:** on the Phase 1 pipeline runs page, add a column and a chart for how long each run waited for an agent compared with how long it ran.

**History collection**
ADO keeps only limited job history and `jobrequests` returns recent jobs. To get real trends, the `AgentJobCollector` background service:
- polls each pool's `jobrequests` every N minutes (configurable),
- upserts finished jobs into SQLite keyed on `requestId`,
- records a pool-size snapshot (online agents) at each poll, for the capacity charts,
- prunes rows older than `RetentionDays`.

The charts then read from SQLite and stay fast. Note that history starts when the collector first runs, so turn it on early.

### Phase 3: Run detail and fast routes to the logs
The app **does not fetch or render log content**. Instead, it gets you to the right log in the ADO portal in one click, which avoids pushing large logs through the server. Everything on this page comes from small JSON calls.

| Feature | Source |
|---|---|
| **Run timeline**: stages → jobs → tasks tree with result, duration, agent name and error/warning counts, with failed nodes auto-expanded | `GET .../build/builds/{buildId}/timeline` |
| **"Open log" on every task** | Deep link to the portal's log view focused on that job and task: `https://dev.azure.com/{org}/{project}/_build/results?buildId={buildId}&view=logs&j={jobRecordId}&t={taskRecordId}` (record ids come from the timeline). |
| **"Open first failure"** button at the top of a failed run, and in the runs list | The same deep link, aimed at the first failed task in the timeline. |
| **Error summary**: the error and warning messages from the timeline (`record.issues`) listed at the top of the run page, often enough to see what broke without opening the log at all | timeline |
| **Download all logs (zip)** link | A plain browser link to `https://dev.azure.com/{org}/{project}/_apis/build/builds/{buildId}/logs?$format=zip`. The download comes straight from ADO using your browser's existing ADO sign-in, not through the app. |
| Copy-link buttons for a run, job or task | built from the same URLs |
| Artifacts list, with links into the portal | `GET .../build/builds/{buildId}/artifacts` |
| Changes in this run (commits) | `GET .../build/builds/{buildId}/changes` |

> The portal URL format is not a formal API, so all ADO links are built in one `AdoLinks` helper. If Microsoft changes the format, there's one place to fix.

### Phase 4: Repo policies
| Feature | REST endpoint(s) |
|---|---|
| **Policies on a repo/branch**: min reviewers, required reviewers, build validation, comment resolution, linked work items, merge strategy | `GET {project}/_apis/policy/configurations?repositoryId={id}&refName=refs/heads/main` |
| Friendly names and settings rendering for each policy type | `GET {project}/_apis/policy/types` |
| **Policy audit matrix**: every repo as a row, each policy as a column, highlighting repos whose default branch has *no* PR policy, too few reviewers or no build validation | the same calls, fanned out across repos |

### Phase 5: "What needs my attention?" dashboard (read-only)
- **My PRs and PRs awaiting my review** across all projects: `GET _apis/git/pullrequests?searchCriteria.reviewerId={me}&searchCriteria.status=active`.
- **Pending pipeline approvals for me**: `GET {project}/_apis/pipelines/approvals?state=pending`. These are listed with a link to approve in ADO; there's no in-app approve button.
- **Red board**: every pipeline whose latest run on the default branch failed, across projects.
- **Agent pool tile**: queued jobs right now and current p90 wait for each pool, from Phase 2.
- **Watchlist**: pin favourite pipelines, repos and pools, stored in the local SQLite database.

### Further ideas (backlog, roughly by value)
1. **Flaky test detector**: use the Test Runs/Results APIs to find tests that flip between pass and fail on the same commit or branch.
2. **Pipeline trends**: duration and pass rate over 30/90 days.
3. **Error search**: search the timeline error messages (`record.issues`) across the last N runs ("when did this error first appear?"). This uses timeline data, not log files.
4. **Compare two runs**: diff of commits, parameters, variables, agent used and failed tasks between a good run and a bad one.
5. **Capacity what-if**: replay the stored job history against a hypothetical pool size to estimate how much adding N agents would cut p90 wait.
6. **Service connections and variable groups inventory**: who uses what, plus a search for which pipelines reference a given variable group (`serviceendpoint/endpoints`, `distributedtask/variablegroups`).
7. **Stale branch report**: branches with no commits in X days, or already merged into main.
8. **Release notes generator**: work items and commits between two runs or tags, exported as Markdown.
9. **Cross-repo code search** (`almsearch.dev.azure.com/.../_apis/search/codesearchresults`).
10. **YAML viewer**: show the pipeline's YAML (fetched from the repo) with template references linked.
11. **Notifications**: a background service watches the watchlist and pushes toast notifications when a watched pipeline fails or a pool's wait time spikes.
12. **Multi-org support**: the config already allows it; the picker switches the org.

---

## 5. Design notes and gotchas

- **Throttling:** ADO rate-limits per user (TSTUs). Fan-out views (policy audit, red board, collector polling every pool) should limit concurrency (`Parallel.ForEachAsync` with `MaxDegreeOfParallelism ≈ 4–8`), cache results and honour `Retry-After`.
- **Paging:** list endpoints cap results (often 100–1000) and return a continuation token. Always go through the paging helper.
- **Logs stay in ADO:** the app never downloads log content, so there's no large-payload streaming through the Blazor circuit. If an in-app log view is ever wanted, it would be a separate, opt-in feature.
- **Timestamps:** ADO returns UTC. Store UTC and convert to local time only in the UI, which matters for the day × hour heatmap. Treat jobs with missing `assignTime` (cancelled while queued) separately rather than as zero wait.
- **Microsoft-hosted pools:** wait time there reflects your *parallel job limit*, not machine count, so label those charts that way.
- **Blazor Server circuits:** keep per-user state in scoped services, and dispose of `CancellationTokenSource`s when components are disposed so abandoned page loads stop calling ADO.
- **Errors:** surface 401/403 with an actionable message ("Your PAT lacks the *Agent Pools (Read)* scope").
- **Deep links:** every entity shows an "Open in Azure DevOps" link, so the app augments the portal rather than competing with it.
- **Security:** never log tokens, keep the PAT out of `appsettings.json`, and if hosted, put it behind Entra ID auth (`Microsoft.Identity.Web`).

---

## 6. Suggested order of first PRs

1. Solution skeleton, Fluent UI layout, CI workflow.
2. `DevOpsBlazor.AzureDevOps`: auth handler, read-only guard, error mapping, paging helper and tests.
3. Health page and project picker.
4. Repository list.
5. Pipeline list → pipeline runs page (last N, pass/fail, queue wait, sparkline).
6. Agent pools overview and agent list (live status).
7. Job history: the `jobrequests` client, wait/run stats in Core, and the pool detail page.
8. SQLite store and `AgentJobCollector`, then trend charts and the heatmap.
9. Run detail: timeline tree, error summary, `AdoLinks` with "open log" / "open first failure" / zip links.
10. Repo policies page, then the policy audit matrix.
11. Dashboard: my PRs, approvals, red board, pool tile.

---

## 7. Open questions

- One ADO organisation or several?
- Local-only (`dotnet run`) or hosted (Azure App Service with Entra login)? Hosting decides when Phase 2 auth is needed. It also matters for the collector, which only gathers history while the app is running.
- Is a PAT acceptable to start with, or is there an org policy restricting PATs?
- Mostly self-hosted agent pools, Microsoft-hosted, or both?
- Do you have org-level permission to view agent pools? *Agent Pools (Read)* on the token is needed, but so is pool access in ADO.
- What p90 wait time would you consider "bad"? It sets the default alert threshold.
