# Northwind Support Desk

.NET 10 Web API, React 19/TypeScript and SQL Server. Tasks 1-3 are implemented.
Task 4 is in progress: the API Dockerfile/build context are added; Compose integration
and database health reporting are next. The optional SLA report is deferred.
Requirements: [assessment PDF](ASSIGNMENT_CANDIDATE.pdf). Design reasoning, verification
and the step-by-step record are in [APPROACH.md](APPROACH.md).

## Run and log in

Install the .NET SDK from `global.json`, Node from `.nvmrc` (Node 22), and Docker.
Run from the repository root:

```powershell
npm ci
npm run db:up
npm run api                    # terminal 1, after SQL Server is healthy
npm run web                    # terminal 2
```

Open [the web app](http://localhost:5173/login). Public local development credentials:

```text
Email:    alex.turner@northwind.com
Password: northwind_alexturner
```

Ben, Priya, Sara and Marcus retain `Northwind_LocalDemo_Only1!` at their seeded
`first.last@northwind-support.example` addresses. Yuki is inactive and cannot sign in.
These are demo credentials, never production credentials. SQL Server's separate
local fixture is `sa` / `Local_Dev_Password1`.

The API listens at `https://localhost:7043` and `http://localhost:5043`;
[Swagger](https://localhost:7043/swagger) stays open in Development. Vite proxies
`/api` to the API. If necessary, set `VITE_API_PROXY_TARGET=http://localhost:5043`
in the web terminal before starting it. Stop the database with `npm run db:down`.

Development startup applies migrations and seeds 40 tickets, six agents, six
customers and five categories. Existing demo databases receive missing password
hashes without resetting established credentials or ticket data. Alex's previous demo
email/shared password are upgraded in place to the credentials above; his agent ID and
ticket relationships are preserved. This change is applied once, not on every restart.
Compose reads
`.env` if you copy `.env.example`; `dotnet run` does not load it automatically.
Set process environment variables such as `$env:ConnectionStrings__Default` to
override API settings. Never commit `.env` or real credentials. Visual Studio:
open `SupportDesk.sln`, select SupportDesk.Presentation and use the https profile.

## API image: Task 4, step 1

Build from the repository root, because the API references projects and shared build
metadata outside its own directory:

```powershell
docker build -f apps/api/Dockerfile -t northwind-support-api:dev .
docker run --rm --entrypoint whoami northwind-support-api:dev
docker image ls northwind-support-api
```

The SDK stage restores using only the project files, central package/build properties
and global.json, then copies source and publishes Release output. A source-only change
therefore preserves the restore cache. The final ASP.NET 10 image contains published
output, runs as the image's non-root `app` user, and exposes HTTP port 8080.

The root .dockerignore admits only API sources and required .NET metadata. It excludes
host build outputs, the React app, .git, .env files, environment-specific appsettings,
launch settings and certificates. Shared appsettings.json stays for the SLA defaults;
database, JWT and environment settings must be supplied at runtime. The next steps
add `/health` and Compose wiring for the database/configuration.

Step 1 verification: the image built successfully; `whoami` returned `app` (UID
1654), no SDK was installed, and only shared appsettings.json was published. An
isolated rebuild with one C# comment changed reused the restore layer and reran
publish. Docker-reported image sizes were 433.8 MiB for the API and 1243.4 MiB for
the SDK baseline (about 65% smaller). These are local image sizes, not download sizes.
Restore/publish reported NU1902/NU1903 warnings for the existing transitive
System.Security.Cryptography.Pkcs 9.0.11 dependency; this Docker step does not resolve
those dependency warnings. Full container startup with SQL is verified in the
later Compose step.

## What changed

**Task 1:** DEFECT-117 came from a fetching effect that watched search and pagination
but omitted other filters, so changing Status alone sent no request. The hook now
observes the complete filter object; dropdowns request immediately, search alone
is debounced 300 ms, and older responses cannot replace newer results. A regression
test asserts the request and failed against the original hook.

`TicketQueries.ApplyFilters` applies exact-match status/priority/category/customer/
agent, unassigned-only, title/reference/customer-name search and SLA predicates in
SQL before COUNT, sorting and paging. Filters combine with AND; High means High only.
Relational tests inspect query results and generated COUNT/page SQL. To check manually,
change Status alone with Network open, then combine filters and inspect the API SQL log.
New filters belong in this same query composition method.

**Task 2:** Creation respects requested priority (default Medium), except flagged
categories force Critical. Base SLA windows are Critical 4h, High 8h, Medium 24h and
Low 72h; Premium halves the window with a 1h floor. Active agents must be strictly
below capacity and specialized where required. Fewest open tickets wins, then lowest
ID; no eligible agent is a successful unassigned outcome with an explanation.
Escalation raises priority one step, recomputes the deadline from now, retains an
eligible owner or reassigns, and writes immutable before/after history. Critical,
Resolved and Closed tickets return 409; bad reasons return 400; missing tickets 404.

SLA status is derived with an injectable clock: No SLA, Met, Breached, At risk
(25% or less remaining), or Within SLA. Exactly at the deadline is At risk; one tick
after is Breached. The UI shares one labelled indicator, filters SLA in SQL, shows
triage after creation, and supports escalation/history with validation, pending,
error/retry and conflict states without a full reload.

**Task 3:** Agents sign in, all existing API endpoints require a valid token through
one fallback policy, and escalation records the authenticated agent. The browser
provides login, protected routes, signed-in name, logout and expired-session handling.

## Authentication and design decisions

Credentials use a nullable `Agent.PasswordHash` column because existing agents are
the only users; a second Users table adds no useful identity mapping here. Null
supports migration of existing rows and cannot authenticate. ASP.NET Core Identity's
`PasswordHasher<Agent>` hashes and salts each development password at seed time;
only the encoded hash reaches the database. Verification supports rehashing when
Identity requests an upgrade. Passwords/hashes are never returned in API DTOs.

`POST /api/auth/login` accepts email/password and returns `accessToken`, `expiresAtUtc`
and `user` (id, fullName, email). Email is trimmed and matched case-insensitively;
passwords are not trimmed. Unknown email, wrong password, inactive agent and missing
credentials share a generic 401. Invalid field shapes return 400. `/api/auth/me`
returns the current active agent or 401. Protected calls use `Authorization: Bearer`.

JWTs use HS256, a 60-minute lifetime, zero expiry grace, and a stable agent ID in
`sub`, plus name/email/jti. `Jwt:Issuer`, `Audience`, `SigningKey` and `LifetimeMinutes`
come from configuration; `.env.example` lists the environment overrides. Startup
requires issuer/audience, a key of at least 32 UTF-8 bytes, and a lifetime of 1-1440
minutes. The Development key is a public local fixture. Supply a separate random
`Jwt__SigningKey` outside Development, never a literal in C# or a committed real secret.
Middleware checks signature/algorithm/issuer/audience/lifetime and active-agent status.

Escalation accepts only `{"reason":"Customer cannot continue working."}`. The
controller passes the validated agent ID separately to the application handler;
history stores `agent:<id>`, which survives email/name changes and fits the existing
100-character column. Extra caller-supplied actor fields are ignored. The form has
no editable actor. Existing history is preserved.

The shared frontend client attaches tokens and clears the matching session on any
protected-call 401. Session data lives in memory and `sessionStorage` under
`northwind.auth.session`, surviving refreshes within a tab; passwords are never saved.
Refresh validates `/me` before protected pages mount. Transient failures offer retry
without losing credentials; expiry timers/focus checks and logout clear the session.
Login returns to a safe local destination. Delayed responses cannot overwrite or
invalidate a newer session. If storage is blocked, login works in memory only.

Trade-off: JavaScript can read sessionStorage, so XSS can steal a token. A production
session design could use HttpOnly cookies with CSRF protection. Client guards are
navigation behavior; the API enforces authorization. Logout clears this tab, without
revoking copied tokens or signing out other tabs. A copied token may remain valid
until expiry unless its agent is deactivated. Refresh tokens/revocation are out of scope.

Priority, SLA and assignment policies are plain domain logic shared by creation and
escalation; application handlers coordinate queries and one save; controllers map HTTP.
Configuration supplies SLA values and category flags, rather than category-name checks.
One expression supplies runtime/SQL SLA evaluation. I kept abstractions at database/
authentication seams and rejected per-policy interfaces, event sourcing and new state
libraries. The frontend uses a small subscribed store with `useSyncExternalStore`.

## Database and API

`TicketEscalationAndSlaWindows` adds history, window timestamps and ticket revision
concurrency. `IX_TicketEscalations_Ticket_Time_Id` supports stable newest-first history;
restricted foreign keys, EF guards and a SQL Server trigger protect it. Updates and
history inserts share a transaction, and stale changes return 409. `AgentPasswordCredentials`
adds nullable `PasswordHash` (512 characters). Run `npm run db:migrate` explicitly if needed.

Protected endpoints cover ticket list/detail/create/status/assignment, customer
list/detail, agents, categories, escalation and history. Create returns 201 with
triage; `POST /api/tickets/{id}/escalate` returns the updated ticket and recorded
escalation; `GET /api/tickets/{id}/escalations` returns newest first. Lists accept
`search`, `status`, `priority`, `categoryId`, `customerId`, `assignedAgentId`,
`unassignedOnly`, `slaStatus`, `sortBy`, `sortDirection`, page and pageSize.
Errors use 400 validation, 401 authentication, 404 missing resource and 409 conflict.

## Verify

```powershell
$env:Configuration = 'Release'   # avoids Debug file locks when the API is running
npm test
npm run lint
npm run typecheck
npm run build
```

Tests need no external database: 186 backend unit/integration tests, four architecture
tests, 22 HTTP authentication tests and 77 frontend tests (289 total). On Windows,
`dotnet test --configuration Release -m:1 -p:UseSharedCompilation=false` is an
alternative backend command if compiler workers stall. The full repository build
passed; existing NU1902/NU1903 warnings for transitive Pkcs 9.0.11 remain.

Tests reuse FixedClock/TicketBuilder and isolated SQLite, covering query SQL, rules,
SLA boundaries, rollback/concurrency/history, authentication and forgery. Frontend
tests use the real client/routes with mocked HTTP, including refresh, logout, 401,
corrupt storage, safe redirects and old-session races. Desktop/mobile browser checks
also passed with mocked API responses. Final isolated SQL Server checks passed
36 live API assertions and verified fresh and existing-schema migrations. Real
browser/API/SQL checks passed login, escalation, history, refresh, 401 recovery and
logout; the temporary database and servers were removed afterwards.

## AI usage, limitations and submission

Codex assisted with implementation, tests, debugging, SQL/browser checks and docs.
Small changes against the PDF followed by relational/HTTP/UI tests worked well.
The generated shared-policy structure was retained; a generated EF history projection
was reordered after translation failed, and stale-response handling was corrected.
Task 3 tests also exposed configuration binding before test-host overrides; resolution
now occurs against the built host. Redirect validation was tightened for encoded and
trailing-slash login paths. I verified generated code with tests, types, lint, builds,
SQL checks and browser checks, and must be able to explain the submitted code.

Task 4 remains unfinished: an API image is added, while Compose still runs SQL Server
only and `/health` is pending. A real deployment should run migrations as a controlled deployment job,
not rely on Development startup/seeding. The optional report is deferred. Cross-ticket
assignment uses a workload snapshot and can exceed capacity under simultaneous creates;
the reference generator retains its concurrency limitation. Triage reasons are returned
but not persisted for later GETs. Registration, password reset, roles, OAuth/SSO,
refresh tokens, MFA and rate limiting were deliberately not added.

Keep the focused commits and merge feature PRs into your fork's main with a merge
commit. Before submitting, include the fork/PR link, development login and your actual
rough time spent; assignment estimates are not a record of time spent. Read
[APPROACH.md](APPROACH.md) for implementation details and trade-offs.
