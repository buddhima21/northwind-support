# Task 1 approach

## Defect fix

The report concerns missing requests, so the regression tests inspect fetch URLs,
not whether the table happens to contain a matching ticket. The status-only test
failed on the original hook because the effect omitted status from its dependencies.

Keep immediate UI search text separate from the settled request filters. Debounce
only search, and use the complete request-filter object as the fetch dependency.
This avoids a hand-maintained list of effect dependencies when new filters arrive.
A request sequence prevents late responses from replacing the latest results.

I rejected adding a component remount, page reload, or client-side row filtering:
none repairs the missing request. I also avoided a new state-management dependency.

## Database filtering

Keep filtering in the existing infrastructure query, where EF can translate it to
SQL. A single ApplyFilters method composes WHERE clauses before the query branches
into COUNT and the sorted page. Controllers and React components contain no database
filtering rules. Priority uses equality, rather than a minimum-priority comparison.

Search trims whitespace and matches title, reference or customer name. Separate
filters use AND, including the contradictory assigned-agent plus unassigned-only
combination. Search follows the database's collation rather than forcing one in code.
Existing indexes are sufficient for this assignment-sized change; no migration or
new abstraction is justified. Task 2 can add its SLA predicate in the same method.

Tests execute real relational queries against isolated SQLite databases, avoiding
mocked IQueryable behavior and any modification to the demo SQL Server database.
The tests also inspect logged COUNT and page SQL to check where filtering happens.

# Task 2 — Ticket Escalation & Assignment

## Overall approach

The aim was to automate the decisions a team lead makes when a ticket arrives and
provide a clear record when it needs escalation. I kept the business rules in small
domain policies shared by creation and escalation. Application handlers coordinate
these policies and persistence, while controllers handle HTTP requests.

## Automatic triage and assignment

PriorityPolicy respects the requested priority, defaults to Medium and uses category
flags to force Critical. SlaPolicy calculates the deadline from priority and customer
tier, halving the window for Premium customers with a one-hour minimum. SLA settings
live in shared appsettings.json and are validated at startup. Category names are never
used to decide priority or specialist requirements.

AssignmentPolicy selects active agents who are below capacity and have the required
specialization. It chooses the fewest open tickets, breaking ties by lowest ID. SQL
queries provide workload counts without loading ticket histories. If nobody qualifies,
the ticket is created unassigned with an explanation; that is a successful outcome.
Creation returns the saved priority, deadline, owner and triage reasons.

## Escalation and history

Escalation raises priority one level and recalculates the deadline from the injected
clock. It keeps the current owner if still eligible; otherwise it applies the same
assignment policy. Critical, Resolved and Closed tickets are rejected with 409.
Reasons and actor identifiers are validated, and the current ticket counts toward
its owner's capacity when checking eligibility.

The migration adds immutable escalation history and ticket timing fields. Ticket
changes and history are saved in one transaction, with a revision token protecting
against stale updates. Restricted foreign keys, EF guards and a SQL Server trigger
protect history. The composite index on ticket ID, escalation time and history ID
supports stable newest-first retrieval. Existing deadlines are preserved.

## SLA status and filtering

SLA status is derived rather than stored. One expression defines the runtime and
SQL rules, so filtering, sorting, counts and paging use consistent decisions. Each
window snapshots its deadline and at-risk cutoff; configuration changes affect future
windows after restart. Tests use FixedClock to check exact threshold and deadline
boundaries without depending on the wall clock.

## Frontend behavior

The frontend reuses one labelled SLA indicator on list and detail pages and adds an
SLA filter, escalation form, history and triage summary. The form blocks invalid input
and duplicate submissions, disables ineligible tickets and displays server errors.
Success updates the ticket and refreshes history without reloading the page. A 409
preserves the draft and displayed ticket, while history failures have their own retry.
Request-order guards prevent older reads from replacing refreshed data.

## Testing and verification

Verification passed 189 backend tests, 45 frontend tests, lint, typecheck, build and
the migration-model check. Relational tests cover filtering, assignment, escalation,
rollback and history; frontend tests cover validation, conflicts and refresh behavior.
Final live checks passed 29 API assertions, and browser checks confirmed filtering,
validation and disabled escalation. A generated history query initially failed EF
translation; relational tests exposed it, and filtering was moved before projection.

## Design decisions and limitations

I avoided extra policy interfaces, event sourcing and a new frontend state library
because the existing boundaries were sufficient. Cross-ticket assignment remains a
workload snapshot, and the starter reference generator retains its concurrency
limitation. The actor is caller-supplied until Task 3 adds authentication. The optional
SLA summary report remains deferred. Existing dependency warnings are documented in
README.md.

## Task 3, step 1: credential storage and development provisioning

I chose a PasswordHash column on Agent because the six agents are the required users;
a separate Users table would introduce a mapping with no current use. The column is
nullable so the migration preserves existing agents without fabricating credentials.
Future login handling must reject missing hashes and inactive agents.

ASP.NET Core Identity's PasswordHasher performs the salted hashing in Infrastructure,
accessed through its built-in IPasswordHasher interface. The domain only accepts an
encoded hash. The ASP.NET shared-framework reference provides the implementation
without introducing the full Identity persistence system or a custom hash algorithm.

The existing seeder skipped all work once tickets existed, so adding hashes only to
fresh seed data would leave the current development database unable to log in. It now
provisions null hashes for the exact six demo email addresses before returning. It
preserves established hashes, other accounts, ticket data and Yuki's inactive status.
Fresh databases hash the documented public development password separately per agent.
Relational tests verify both fresh and existing-database paths and repeated seeding.
