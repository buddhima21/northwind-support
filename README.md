# Northwind Support Desk

.NET 10 API, React 19/TypeScript and SQL Server. Tasks 1–4 are implemented against [the assessment](ASSIGNMENT_CANDIDATE.pdf). [APPROACH.md](APPROACH.md) summarises each task.

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

## Authentication

Credentials live in `Agent.PasswordHash`. `PasswordHasher<Agent>` creates salted hashes and supports rehashing; missing credentials and inactive agents cannot authenticate. Login returns the same generic 401 for invalid credentials. HS256 JWTs contain the stable agent ID, expire after 60 minutes and are validated for signature, issuer, audience, expiry and active-agent status. A fallback policy protects endpoints centrally; login and `/health` explicitly allow anonymous access. Escalation uses the authenticated ID, never a caller-supplied actor.

The client stores token, expiry and profile in memory/sessionStorage, never passwords. Refresh validates `/api/auth/me`; expiry, protected-call 401 and logout clear the session. JavaScript-readable tokens remain vulnerable to XSS. Production would require HTTPS, a separate signing key, abuse protection and a reviewed session/revocation design; HttpOnly cookies with CSRF protection are an alternative.

## How to log in

Use Docker and Node 22 (.NET 10 SDK for host tests/builds). Start the backend as described below, then run:

```powershell
npm ci
$env:VITE_API_PROXY_TARGET = 'http://localhost:5080'
npm run web
```

Open http://localhost:5173/login. Email: **alex.turner@northwind.com**; password: **northwind_alexturner**. Other active agents use `Northwind_LocalDemo_Only1!` at `first.last@northwind-support.example`; Yuki is inactive. These are public development fixtures. Fresh startup seeds 40 tickets, six agents, six customers and five categories; existing demo records are preserved.

## Docker

Run from the repository root; keep an existing `.env` rather than overwriting it:

```powershell
if (!(Test-Path .env)) { Copy-Item .env.example .env }
docker compose up --build -d
# After initial SQL startup and seeding:
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

Last recorded verification passed **289 tests**: 186 backend unit/integration, four architecture, 22 HTTP and 77 frontend. FixedClock/TicketBuilder and isolated SQLite cover rule boundaries, SQL, transactions, concurrency, history, authentication and session races without external SQL. Live SQL/browser checks covered authentication and escalation. Manual Docker verification covered empty-volume migration/seeding, persistence, restore caching, non-root execution, 200/503 health and the frontend proxy. Docker automation, load testing and out-of-scope account features were deliberately excluded.

## AI usage

I used OpenAI Cluade for code/test drafts, Chatgpt for debugging, SQL/browser checks, documentation and explanations. The useful workflow was one requirement, a small change, then diff review and verification before committing. I retained much of the generated shared-policy structure because it reused rules without framework dependencies in the domain.

I changed a generated escalation-history query after relational tests exposed an EF translation failure: filtering/ordering now happen before DTO projection. HTTP tests also exposed JWT configuration being resolved before test-host overrides; resolution moved to the built host while preserving startup validation. Generated code was checked with tests, SQL output, lint, type checking, builds, live API/browser checks and Docker inspection. These corrections were part of verification, not reasons to accept generated output without review.

## Known limitations

The optional SLA report is deferred. Assignment uses a workload snapshot and can exceed capacity under concurrent ticket creation; the starter reference generator also has a concurrency limitation. Triage reasons are returned but not persisted. Logout affects the current tab and does not revoke copied JWTs; refresh tokens, registration, password reset and roles are out of scope. `/health` checks connectivity, not schema completeness. Existing NU1902/NU1903 vulnerability warnings for transitive `System.Security.Cryptography.Pkcs` 9.0.11 remain unresolved.
