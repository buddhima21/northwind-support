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
| `GET /api/tickets` | Paged list with `search`, `status`, `priority`, `categoryId`, `customerId`, `assignedAgentId`, `unassignedOnly`, `slaStatus`, `sortBy`, `sortDirection`. Filters are combined in SQL before counting and paging (Task 1). |
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


## Task 1 implementation

### DEFECT-117: filters did not trigger a request

The fetching effect in `useTicketList` depended only on search, page and page size.
Changing another filter on page 1 therefore changed state without running the effect;
typing in search later sent the previously selected filters.

The hook now observes the complete request-filter object. Only search input is delayed
by 300 ms; dropdowns, sorting and pagination request immediately. A future filter does
not need a new effect dependency. Older responses cannot overwrite a newer result.
While typing, dropdown changes use the last settled search; the final search text is
sent after the debounce finishes. Clearing all filters is immediate.

The regression test was run against the original hook: changing Status sent zero
requests and the test failed. After the fix it passed. The tests inspect the actual
fetch URL rather than mocking the ticket API, including each individual filter,
search debounce, clearing search, pagination, retry, and out-of-order responses.

### Server-side filtering

`TicketQueries.ApplyFilters` builds an `IQueryable` with exact matches for status,
priority, category, customer and assigned agent, plus unassigned-only and trimmed
search across title, reference and customer name. Blank search is ignored. Every
supplied filter is combined with AND, so choosing an agent and unassigned-only
together gives zero matches. Search uses database string matching and collation.

The filtered query feeds both `CountAsync` and the sorted, paged projection. No rows
are loaded for in-memory filtering. Add future filters, including Task 2's SLA filter,
to `ApplyFilters` so count and paging continue to use the same predicates.
No schema change or migration is needed for Task 1; the existing indexes remain.

The query tests use a fresh in-memory SQLite database per test with real EF queries.
They cover every filter, all four priorities, combined filters, all search targets,
blank/no-match searches and filtered pagination. A SQL-log assertion checks that the
priority predicate occurs in both COUNT and the page SELECT before LIMIT, and that
reads leave the change tracker empty. SQLite is a portable relational check; SQL Server
uses its own collation and OFFSET/FETCH syntax.

Run from the repository root:

```powershell
npm test
npm run lint
npm run typecheck
npm run build
```

Manual verification: clear the browser Network log, change Status alone, and confirm
a new `/api/tickets` request containing that status. Try High priority (only High,
not Critical), combined filters, a customer-name search and pagination. In the API
Development console, inspect the SQL: WHERE must restrict both COUNT and SELECT,
and SELECT must apply paging after filtering. Restart the API after backend changes.

For separate commits, group the frontend hook and its test as the defect fix; group
the backend query, contract comment and query tests as server-side filtering.
Commit documentation separately or with the relevant implementation.

Task 1 covers filtering. Task 2 progress is documented below; authentication
and API containerization remain later assessment tasks. See `APPROACH.md` for the Task 1 design decisions.

Verification completed for Task 1: full backend and frontend tests, lint, typecheck
and build passed with `Configuration=Release` in the environment. Release was used
because the already-running development API locked its Debug assemblies. SQL Server
was also checked through a temporary API using GET requests only: all individual
filters, combined filters, customer search, no matches and filtered pagination matched
the existing ticket data. Its logged SQL contained WHERE in COUNT and paged SELECT,
with OFFSET/FETCH for paging. The temporary API was stopped after verification.
Existing NuGet audit warnings for transitive System.Security.Cryptography.Pkcs 9.0.11
remain; this change does not update dependencies. Stop the development API before
running the default Debug build or tests to avoid Windows file-lock errors.


## Task 2 progress: backend complete through Steps 2-7

### Automatic triage

POST /api/tickets applies the shared PriorityPolicy, SlaPolicy and AssignmentPolicy.
The 201 response includes the saved priority, deadline and agent, plus a triage object
containing appliedPriority, priorityReason, slaWindowHours, slaReason and assignmentReason.
Nobody eligible is a normal success with assignedAgent null and an explanation.

Sla configuration in appsettings.json supplies all priority windows, the Premium
multiplier, minimum window and at-risk threshold. Values are validated at startup.
Each new or escalated window snapshots its start and at-risk cutoff; changing the
configuration affects future windows after restarting the API, not existing promises.
No category-name checks determine priority or specialist eligibility.

### Escalation API and history

| Endpoint | Behavior |
| --- | --- |
| POST /api/tickets/{id}/escalate | Raises priority one level, recalculates the SLA from now, rechecks ownership eligibility and appends history |
| GET /api/tickets/{id}/escalations | History newest first, with ID as a stable tie-breaker; 404 for an unknown ticket |
| GET /api/tickets?slaStatus=Breached | Filters in SQL before counting and paging; all five SLA states are supported |

Example escalation body for Task 2:

```json
{
  "reason": "The customer's service is still unavailable.",
  "escalatedBy": "alex.lead"
}
```

Success returns 200 with the updated ticket, triage explanations and the saved
escalation. Validation returns 400 for a trimmed reason outside 5-500 characters or
a missing actor (maximum 100 characters). Unknown tickets return 404. Critical,
Resolved and Closed tickets return 409 with an explanation. Concurrent ticket changes
also return 409 and require a reload. Existing central ProblemDetails handling is reused.

The current owner is retained only while active, specialized when required and
strictly below MaxOpenTickets. The workload includes the current ticket, following
the PDF's strict-below rule. Otherwise the same selection policy used by creation
chooses the replacement, or leaves the ticket unassigned with a reason.

Ticket update and history insertion are saved in one transaction. Revision is an
optimistic concurrency token used by ticket mutations. History has private setters,
EF guards and a SQL Server trigger rejecting UPDATE/DELETE. The composite index
IX_TicketEscalations_Ticket_Time_Id supports lookup by ticket and stable newest-first
ordering; restricted foreign keys preserve ticket and agent history.

### SLA status and database migration

SLA status is calculated, never stored. SlaEvaluator supplies one expression for
both runtime evaluation and the SQL CASE predicate. An unresolved ticket is AtRisk
at its cutoff and exactly at its deadline; one tick after the deadline it is Breached.
Resolution on the deadline is Met. The list captures one IClock instant for filtering,
counting and projection. Sorting by slaStatus also executes in SQL.

The TicketEscalationAndSlaWindows migration adds TicketEscalations and ticket fields
SlaStartedAtUtc, AtRiskAtUtc and Revision. Existing due dates are preserved; legacy
cutoffs are initialized from creation with the original 25% threshold. Due-date-free
tickets remain NotApplicable. The migration was applied to the configured local
database. Development startup applies it automatically on another checkout, or run:

```powershell
npm run db:migrate
```

### Verification and remaining work

- 189 backend tests and 18 frontend tests passed, including boundaries, assignment
  reevaluation, history values/order, failed-save rollback, stale-update conflicts,
  relational SLA filtering, SQL count/paging and the request serializer.
- Lint, typecheck and the full build passed using Release configuration to avoid
  the running development API's locked Debug files.
- SQL Server checks verified both an empty database and upgrade of a legacy ticket.
  Thirty real HTTP checks covered 201/200/400/404/409 responses, concurrent escalation,
  history, SLA filters and sorting. Direct SQL history UPDATE and DELETE were rejected.
  Disposable verification databases were removed afterward.

Step 8 remains: the visible SLA filter, shared indicator, escalation form, history
and triage summary. Transport methods and TypeScript types are ready. For Task 2,
escalatedBy is supplied by the body; Task 3 must derive it from the authenticated user.
The starter reference generator and cross-ticket capacity selection still use snapshots
without global serialization. Existing NuGet audit warnings remain. Changes are
uncommitted until Task 2 is ready for separate focused commits.
