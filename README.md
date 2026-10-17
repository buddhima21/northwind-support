# Northwind Support Desk

.NET 10 API, React 19/TypeScript and SQL Server. Tasks 1–4 and the optional SLA summary report are implemented against [the assessment](ASSIGNMENT_CANDIDATE.pdf). [APPROACH.md](APPROACH.md) summarises each task.

## What I implemented

Server-side filtering, automatic triage and assignment, escalation history, SLA indicators, agent authentication and API containerisation. Requested priority defaults to Medium; category flags can force Critical. SLA windows are Critical 4h, High 8h, Medium 24h and Low 72h. Premium customers receive half the window with a one-hour minimum. Assignment chooses an active, eligible specialist where required, strictly below capacity, with the fewest open tickets; lowest agent ID breaks ties. No eligible agent means an unassigned ticket with an explanation.

Escalation requires a 5–500-character reason, raises priority one level, recalculates the deadline from escalation time and retains the owner only if eligible. Critical, Resolved and Closed tickets return 409. Each escalation records immutable history. SLA status is derived: no deadline means NotApplicable; resolution on/before the deadline is Met, late resolution is Breached; unresolved tickets are Breached after the deadline, AtRisk with 25% or less remaining, otherwise WithinSla.

## Design decisions

Priority, SLA and assignment policies are reused by creation and escalation. Handlers coordinate persistence; controllers translate HTTP. `Ticket.RaiseTriaged` constructs tickets from policy decisions, and one SLA expression serves both runtime and SQL evaluation. Configuration/category flags supply rules. I rejected duplicated controller logic, category-name checks and per-policy interfaces because they add maintenance without useful boundaries.

Agents are the required users, so credentials belong on Agent rather than another table. Identity's password hasher and JWT middleware avoid custom cryptography. Database/authentication interfaces provide implementation seams. The existing frontend client and a small session store were sufficient; Redux, MediatR and event sourcing were unnecessary.

## The defect

DEFECT-117 occurred because fetch-effect dependencies omitted dropdown filters. The hook now observes the complete filter object, debounces only search for 300 ms and ignores stale responses. The request-based regression test failed against the original hook. Verify in Network: changing Status alone sends `/api/tickets?status=...` immediately; search waits for the debounce.

## Filtering

`TicketQueries.ApplyFilters` composes EF predicates before COUNT, sorting and paging. Exact matches, unassigned-only, trimmed title/reference/customer search and SLA predicates combine with AND. Relational SQLite tests inspect COUNT/page SQL, and SQL Server development logs were checked during live requests. Add the next predicate in this method, extend the query/client filter contract and add a relational test; no client-side row filtering is needed.

The authenticated optional endpoint `/api/reports/sla-summary?from=2026-10-01T00:00:00Z&to=2026-11-01T00:00:00Z&groupBy=agent` performs one SQL aggregation; `priority` is the other grouping. Dates are optional and filter creation time using inclusive From/exclusive To, normalized to UTC. Agent is the default grouping and uses the current owner (null means unassigned). Counts include every SLA state; `breachRate` is breached/total (0–1). `averageResolutionHours` averages resolved tickets only, or is null. SLA is evaluated now, not historically at To. Invalid grouping/ranges return 400; empty results return `[]`.

## Authentication

Credentials live in `Agent.PasswordHash`. `PasswordHasher<Agent>` creates salted hashes and supports rehashing; missing credentials and inactive agents cannot authenticate. Login returns the same generic 401 for invalid credentials. HS256 JWTs contain the stable agent ID, expire after 60 minutes and are validated for signature, issuer, audience, expiry and active-agent status. A fallback policy protects endpoints centrally; login and `/health` explicitly allow anonymous access. Escalation uses the authenticated ID, never a caller-supplied actor.

The client stores token, expiry and profile in memory/sessionStorage, never passwords. Refresh validates `/api/auth/me`; expiry, protected-call 401 and logout clear the session. JavaScript-readable tokens remain vulnerable to XSS. Production would require HTTPS, a separate signing key, abuse protection and a reviewed session/revocation design; HttpOnly cookies with CSRF protection are an alternative.

## How to log in

### Run with Docker

Install Node 22 and Docker Desktop with Linux containers enabled. Start Docker Desktop and run these PowerShell commands from the repository root:

```powershell
npm ci
if (!(Test-Path .env)) { Copy-Item .env.example .env }
docker compose config --quiet
docker compose up --build -d
```

Keep `ASPNETCORE_ENVIRONMENT=Development` for automatic migration and demo seeding. Wait for startup to finish; `curl.exe -i http://localhost:5080/health` should return `200 Healthy`. Check startup failures with `docker compose logs --tail 50 api sqlserver`. In a second terminal at the repository root, start the frontend:

```powershell
$env:VITE_API_PROXY_TARGET = 'http://localhost:5080'
npm run web
```

Open http://localhost:5173/login. Email: **alex.turner@northwind.com**; password: **northwind_alexturner**. Other active agents use `Northwind_LocalDemo_Only1!` at `first.last@northwind-support.example`; Yuki is inactive. These are public development fixtures. Fresh startup seeds 40 tickets, six agents, six customers and five categories; existing demo records are preserved.

### Run the API locally instead

Install the .NET 10 SDK specified by `global.json` and run `npm ci` if dependencies are not installed. Start only SQL, then the API in the same terminal:

```powershell
npm run db:up
# Once SQL is healthy (docker compose ps):
dotnet run --project apps/api/src/SupportDesk.Presentation --launch-profile http
```

In a second terminal, run `$env:VITE_API_PROXY_TARGET = 'http://localhost:5043'`, then `npm run web`. The login URL and credentials are the same; Swagger is at http://localhost:5043/swagger. The host API uses `appsettings.Development.json`, not the root `.env`; export `ConnectionStrings__Default` in its terminal if SQL credentials differ from the defaults, using `localhost,1433` as the server.

Stop the frontend/host API with Ctrl+C and stop the Compose backend with `docker compose down`. This retains the SQL volume. Do not use `down -v` unless you intend to delete its data.

### View the optional SLA report

After Docker startup above, reviewers can paste this into PowerShell to log in and display the report:

```powershell
$login = Invoke-RestMethod -Method Post -Uri 'http://localhost:5080/api/auth/login' `
    -ContentType 'application/json' `
    -Body '{"email":"alex.turner@northwind.com","password":"northwind_alexturner"}'

Invoke-RestMethod -Uri 'http://localhost:5080/api/reports/sla-summary?groupBy=agent' `
    -Headers @{ Authorization = "Bearer $($login.accessToken)" } |
    Format-Table agentName,totalTickets,breached,atRisk,withinSla,met,averageResolutionHours
```

For priority groups, change `groupBy=agent` to `groupBy=priority` and the table column `agentName` to `priority`. Add optional `from`/`to` UTC dates to the URL to limit the creation period. This is an authenticated API report: opening its URL without a bearer token returns 401. Full JSON also includes `notApplicable` and `breachRate`; use `ConvertTo-Json` instead of `Format-Table` to see every field.

## Docker

Use the startup commands above. To check backend readiness and the container user:

```powershell
curl.exe -i http://localhost:5080/health
docker compose exec api whoami
```

The API maps localhost:5080 to HTTP 8080 and waits for healthy SQL. Its multi-stage build caches restore and runs as `app`; ignore rules exclude secrets, build output and the frontend. Standalone build: `docker build -f apps/api/Dockerfile -t northwind-support-api:dev .`. Runtime image: **433.8 MiB**, versus **1243.4 MiB** for the SDK.

Runtime variables: `ASPNETCORE_ENVIRONMENT`, `ConnectionStrings__Default`, `Jwt__Issuer`, `Jwt__Audience`, `Jwt__SigningKey`, `Jwt__LifetimeMinutes` and SQL's `MSSQL_SA_PASSWORD` (see `.env.example`). Compose connects to `sqlserver,1433`; update older localhost overrides. Runtime credentials stay outside the image. Development migrates/seeds before serving. Production should run a reviewed migration script/bundle once in a deployment job, avoiding replica races and demo seeding. `docker compose down` preserves data; `down -v` deletes it.

## Database

`TicketEscalationAndSlaWindows` adds escalation history, SLA window fields and ticket revision concurrency; `AgentPasswordCredentials` adds nullable password hashes for existing agents. Ticket updates/history inserts share a transaction; stale updates return 409. Restricted foreign keys, EF guards and a SQL Server trigger protect history. `IX_TicketEscalations_Ticket_Time_Id` on `(TicketId, EscalatedAtUtc, Id)` supports per-ticket history retrieval with stable newest-first ordering. Development startup migrates automatically; `npm run db:migrate` is available for explicit local migration.

## Testing

```powershell
$env:Configuration = 'Release'
npm test
npm run lint
npm run typecheck
npm run build
```

Recorded verification covers **304 tests**: 194 backend unit/integration, four architecture, 29 HTTP and 77 frontend. FixedClock/TicketBuilder and isolated SQLite cover rule boundaries, SQL, transactions, concurrency, history, authentication and session races without external SQL. Report tests verify one aggregation per request, grouping, UTC boundaries, all SLA states and nullable averages; live SQL Server results also matched ticket data. Live SQL/browser checks covered authentication and escalation. Manual Docker checks covered empty-volume migration/seeding, persistence, caching, non-root execution, health and the frontend proxy. Docker automation, load testing and out-of-scope account features were deliberately excluded.

## AI usage

I used Claude for code/test drafts and OpenAI Codex/ChatGPT for implementation, debugging, SQL/browser checks, documentation and explanations. The useful workflow was one requirement, a small change, then diff review and verification before committing. I retained much of the generated shared-policy structure because it reused rules without framework dependencies in the domain.

I changed a generated escalation-history query after relational tests exposed an EF translation failure: filtering/ordering now happen before DTO projection. HTTP tests also exposed JWT configuration being resolved before test-host overrides; resolution moved to the built host while preserving startup validation. Generated code was checked with tests, SQL output, lint, type checking, builds, live API/browser checks and Docker inspection. These corrections were part of verification, not reasons to accept generated output without review.

## Known limitations

Assignment uses a workload snapshot and can exceed capacity under concurrent ticket creation; the starter reference generator also has a concurrency limitation. Triage reasons are returned but not persisted. Logout affects the current tab and does not revoke copied JWTs; refresh tokens, registration, password reset and roles are out of scope. `/health` checks connectivity, not schema completeness. The optional report is API-only and reflects current assignment/SLA rather than historical snapshots. Existing NU1902/NU1903 vulnerability warnings for transitive `System.Security.Cryptography.Pkcs` 9.0.11 remain unresolved.
