# Northwind Support Desk

A support ticket management system: raise tickets, search and filter them, assign them to agents,
and move them through their lifecycle.

**.NET 10 Web API** + **React 19 / TypeScript** + **SQL Server**, in one monorepo.

---

## The assignment

You are joining a team that already has this app working. Complete the tasks **in order**.
Full requirements are in **[`ASSIGNMENT_CANDIDATE.md`](ASSIGNMENT_CANDIDATE.md)**.

| # | Task | Done looks like | Effort |
| --- | --- | --- | --- |
| 1 | **Fix a defect, add server-side filtering** | Root cause fixed with a regression test; every list filter applied in the database with a correct count | ~2.5 h |
| 2 | **Ticket escalation & assignment** | Automatic triage on create, escalation with history, SLA status, UI for all of it | ~7 h |
| 3 | **Login and authentication** | Agents sign in; API rejects unauthenticated calls; login UI | ~4 h |
| 4 | **Dockerise the API** *(optional)* | `docker compose up` gives a migrated, seeded API with no baked-in secret | ~1.5 h |

### Guidance

- **Time: 2 days.** Honestly marking a task "not finished" beats rushing all of them.
- **Fork** this repo to your own GitHub account and work there. **Do not push to the original.**
- **AI tools are allowed.** You must understand and be able to defend every line you submit.
- **Commit as you go** and keep the real history. Commit the Task 1 defect fix and the filtering
  separately from each other and from the feature work. A single squashed commit is not accepted.
- **Where the spec is ambiguous**, make a decision, write it down in the README, and move on.
- **Out of scope:** registration, password reset, roles, OAuth/SSO, refresh tokens, MFA,
  notifications, CI, caching, visual redesign. Mention them under *Known limitations* instead.

### Submission

1. Update this **README** with what you built, your design decisions (chosen, rejected and why),
   the defect's root cause, dev login credentials, how to run and test it, AI usage, and known
   limitations. A reviewer must be able to run it without asking you anything.
2. Add an **APPROACH.md** explaining your approach and thought process for each task.
3. Commit both, then send us the **link to your fork**, the **dev login credentials**, and a
   **rough note of time spent** on what. (If the fork is private, add the reviewers as collaborators.)

---

## Quick start

**Prerequisites:** .NET SDK 10 (`global.json`), Node.js 22 (`.nvmrc`; 20.19+ works), Docker.

```bash
cp .env.example .env          # optional, every value has a working default
npm install                   # installs the web app
npm run db:up                 # SQL Server 2022 in Docker on localhost:1433
npm run api                   # terminal 1: migrates, seeds, then serves the API
npm run web                   # terminal 2: React dev server
```

| What | Where |
| --- | --- |
| Web app | http://localhost:5173 |
| API | https://localhost:7043 (also http://localhost:5043) |
| Swagger | https://localhost:7043/swagger |

In Development the API applies migrations and seeds demo data on startup. **Never commit real
credentials.**

**Visual Studio 2026:** run `npm run db:up`, open `SupportDesk.sln`, set
**SupportDesk.Presentation** as the startup project, and press F5 on the **https** profile.

## Tests

```bash
npm test                      # dotnet test, then the web tests (Vitest)
```

Both pass on a clean checkout and neither needs a database.

## Commands

| Command | What it does |
| --- | --- |
| `npm run db:up` / `db:down` / `db:reset` | Start / stop / wipe SQL Server |
| `npm run api` / `npm run web` | Run the API / the React app |
| `npm run build` | Build both |
| `npm run lint` / `typecheck` / `format` | Web lint and types / `dotnet format` |
| `npm run ef -- migrations add <Name>` | Add an EF Core migration |

## API at a glance

| Endpoint | Description |
| --- | --- |
| `GET /api/tickets` | Paged list with `search`, `status`, `priority`, `categoryId`, `customerId`, `assignedAgentId`, `unassignedOnly`, `slaStatus`, `sortBy`, `sortDirection`. Filters are applied in SQL before counting and paging; see the implementation notes below. |
| `GET /api/tickets/{id}` | One ticket |
| `POST /api/tickets` | Create a ticket |
| `PATCH /api/tickets/{id}/status` · `/assignment` | Change status / assignee |
| `GET /api/customers` · `/api/agents` · `/api/categories` | Reference data |

Errors are `ProblemDetails`: `400` validation, `404` not found, `409` conflict or business rule.

**Seed data:** 5 categories, 6 customers (3 Premium), 6 agents (one inactive, one at their limit)
and 40 tickets across every status, priority and SLA state.

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| API cannot reach the database | `npm run db:up`, wait ~20 s, check `docker compose ps` |
| Port 1433 already in use | Change the port in `docker-compose.yml` and `.env` |
| Browser warns about the certificate | `dotnet dev-certs https --trust`, or use the web app, which proxies |
| Web app shows "Unable to load tickets" | The API is not running, or `VITE_API_PROXY_TARGET` points at the wrong port |
| SQL Server slow on Apple Silicon | Enable Rosetta in Docker Desktop → Settings → General |

---

## Candidate implementation

This section records the work completed for this assessment against the supplied
[assignment PDF](ASSIGNMENT_CANDIDATE.pdf). [APPROACH.md](APPROACH.md) explains the
reasoning in more detail.

### Completed work

| Task | Current status |
| --- | --- |
| Task 1: defect fix and server-side filtering | Implemented, tested and merged |
| Task 2: ticket escalation and assignment | Backend and frontend implemented and verified |
| Task 3: login and authentication | Not implemented yet |
| Task 4: API containerization | Not implemented yet; Compose starts SQL Server only |
| Optional SLA summary report | Deferred until Tasks 1-3 are complete |

### Task 1: defects and server-side filtering

**DEFECT-117:** the ticket-list fetching effect observed search, page and page size
but omitted the other filters. Changing Status on page 1 therefore changed the
control without requesting new results. The hook now observes the complete filter
object. Dropdowns request immediately; search alone is debounced by 300 ms. A request
sequence prevents older responses replacing newer results. The status-only regression
test failed against the original hook and passed after the fix.

**DEFECT-104 / priority filtering:** the starter API accepted filters without applying
them. `TicketQueries.ApplyFilters` now combines exact-match priority, status, category,
customer, agent, unassigned-only, search and SLA predicates with AND. High returns
only High. Search matches title, reference or customer name using database collation.
Filters precede both COUNT and sorted paging. Relational tests inspect generated SQL;
SQL Server checks also verified results and paging. New filters belong in this same
method so the count and returned page stay consistent.

To verify the defect fix, clear the browser Network log and change Status alone.
A new `/api/tickets` request should contain the selected status. Check High priority,
combined filters, customer-name search and pagination; inspect the API SQL log for
WHERE in both COUNT and the paged SELECT.

### Task 2: business rules and frontend

Creating a ticket now determines priority, deadline and owner automatically and returns
human-readable triage reasons. Requested priority defaults to Medium; category flags
can force Critical. Default SLA windows are Critical 4h, High 8h, Medium 24h and Low
72h. Premium customers receive half the window, with a 1h minimum. Assignment selects
active agents strictly below capacity, requiring specialization where the category
specifies it. Fewest open tickets wins, then lowest ID. If nobody qualifies, creation
still succeeds with an unassigned ticket and an explanation.

Escalation raises priority one level, recalculates the deadline from the injected
clock, keeps an eligible owner or reassigns, and saves immutable history. Critical,
Resolved and Closed tickets return 409; missing tickets return 404. Trimmed reasons
must contain 5-500 characters and actor identifiers 1-100. History is returned newest
first with before/after priority, owner and deadline, reason, actor and time.

SLA status is derived rather than stored: no deadline means No SLA; on-time resolution
means Met; late resolution or an unresolved ticket past due means Breached. An
unresolved ticket with 25% or less of its window remaining is At risk; otherwise it
is Within SLA. Exactly at the deadline is At risk, and one tick after is Breached.
The new `slaStatus` parameter filters in SQL before counting, sorting and paging.

The UI uses one labelled SLA indicator on list and detail pages, an SLA filter,
a validated escalation form, history and a create-time triage summary. It disables
ineligible escalation and pending duplicate submissions, displays server errors and
refreshes the saved ticket and history without a page reload. A 409 retains the draft
and displayed ticket with an explicit reload action. History reads have their own
loading, empty, error and retry states; stale reads cannot overwrite a refresh.

### Design decisions and alternatives

PriorityPolicy, SlaPolicy and AssignmentPolicy are plain domain policies shared by
creation and escalation. Application handlers coordinate database queries and a single
save; controllers handle HTTP. Database interfaces provide useful seams, while an
interface for every pure policy, event sourcing and a new frontend state library
would add complexity without helping these requirements.

SLA values live in shared `appsettings.json`, with environment overrides and startup
validation, so all environments receive the same defaults. Category flags replace
name-based checks. Windows snapshot their deadline and at-risk cutoff; configuration
changes affect future windows after restart. One expression supplies both runtime
SLA evaluation and SQL predicates. The current ticket counts toward its owner's
capacity when checking eligibility, following the strict-below requirement.

### Database and API changes

Migration `20261016094553_TicketEscalationAndSlaWindows` adds TicketEscalations and
ticket fields `SlaStartedAtUtc`, `AtRiskAtUtc` and concurrency token `Revision`.
It preserves legacy deadlines and backfills cutoffs using the original 25% threshold.
History has configured keys, required values, length limits and restricted foreign
keys. `IX_TicketEscalations_Ticket_Time_Id` supports lookup by ticket and stable
newest-first ordering. EF guards and a SQL Server UPDATE/DELETE trigger protect
history. Ticket updates and history insertions share one transaction; stale revisions
return 409. Development startup applies migrations, or run `npm run db:migrate`.

| Endpoint | Added behavior |
| --- | --- |
| `POST /api/tickets` | 201 with saved ticket and triage explanations |
| `POST /api/tickets/{id}/escalate` | 200 with saved ticket, triage and recorded escalation |
| `GET /api/tickets/{id}/escalations` | Newest-first history; 404 for an unknown ticket |
| `GET /api/tickets?slaStatus=Breached` | SQL filtering with matching count and pagination |

Example escalation body:

```json
{"reason":"Customer cannot continue working.","escalatedBy":"alex.lead"}
```

### Setup, authentication and Docker status

Use the original Quick start above and wait for SQL Server to become healthy before
starting the API. Default SQL credentials are `sa` / `Local_Dev_Password1`, a local
fixture, not an application login. Compose reads `.env`; `dotnet run` does not load
it automatically. Set `$env:ConnectionStrings__Default` in the API terminal to
override its database connection. Never commit `.env` or real credentials.

Task 3 is pending: there is no login screen, password hashing, JWT issuance/storage,
token lifetime or development application login yet. For Task 2, `escalatedBy` comes
from the form/request body; authentication must replace it with the token identity.
Task 4 is also pending: there is no API image, image-size measurement or container
migration workflow to report. The existing Compose service runs SQL Server, and the
API runs through the .NET SDK.

### Testing and verification

Run these commands from the repository root. Release avoids Windows Debug assembly
locks if the development API is running:

```powershell
$env:Configuration = 'Release'
npm test
npm run lint
npm run typecheck
npm run build
```

Verification on 2026-10-16 passed **185 backend unit/integration tests, 4 architecture
tests and 45 frontend tests**, lint, typecheck, full build and whitespace checks.
EF confirmed no model changes missing from the migration. Backend tests use FixedClock,
TicketBuilder and isolated SQLite databases to cover priorities/tiers, the SLA floor,
assignment eligibility/ties, escalation/rejections, rollback, concurrency, history and
exact SLA boundaries. SQL tests check filtering before count and paging. Frontend tests
use the real API client with mocked HTTP to cover validation, 409 preserving the
screen, pending submissions, refresh/retry, SLA labels and triage outcomes.

Final live checks passed 29 assertions for SLA/priority filters, paging and 400/404/409
responses. Browser checks confirmed At risk filtering, detail indicators, short-reason
validation and disabled escalation. Earlier disposable SQL Server checks covered empty
and legacy database migrations, successful creation/escalation, concurrency and history
immutability. No successful mutations were performed in the final review pass.

A permanent end-to-end browser suite and load/global capacity-reservation tests were
not added; page tests cover UI save/error flows, and global reservation is outside
this implementation. For a manual check, create a Security ticket for a Premium
customer and inspect Critical/2h triage. Escalate an eligible ticket and refresh to
check persistence. Open the same High ticket in two tabs, escalate in the first and
submit from the unrefreshed second: expect 409, then use Reload ticket.

### AI usage

Codex assisted with planning, implementation, tests, debugging, SQL checks and docs.
A useful workflow was implementing small pieces against the PDF and checking each
with relational tests and HTTP calls. The generated deterministic policy structure
was retained largely as-is. A generated history query was changed after relational
tests exposed an EF translation failure: filtering and sorting now happen before DTO
projection. Review also found an older history request could overwrite refreshed data;
request guards and regression tests fix it. Generated code was checked through tests,
types, lint, builds, SQL/HTTP assertions and browser checks. The candidate must review
and be able to explain the submitted code.

### Known limitations and remaining submission work

Authentication and API packaging remain unfinished; the actor is caller-supplied.
Assignment uses a workload snapshot, so simultaneous different tickets can exceed
capacity. The starter reference generator retains its concurrency limitation. History
stores agent IDs, so displayed names reflect current agent names. Triage reasons are
returned for create/escalate operations and are not persisted for later GET requests.
The optional SLA report remains deferred. Existing NU1902/NU1903 warnings for transitive
`System.Security.Cryptography.Pkcs` 9.0.11 remain; Task 2 changed no dependencies.

Keep the separate implementation commits and include the actual time spent with the
final submission. The original effort estimates above are not a record of time spent.
