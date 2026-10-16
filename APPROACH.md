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






## Task 2 - Step 2: priority and SLA policies

PriorityPolicy uses the category's ForcesCriticalPriority flag; otherwise it respects
the requested priority or defaults to Medium. It returns a reason alongside the result.
SlaPolicy calculates the window and UTC deadline from priority, customer tier and an
explicit start instant. Passing IClock.UtcNow from a later use case will let creation
and escalation share this rule without duplicating deadline calculations.

The existing Sla defaults were moved from appsettings.Development.json to the shared
appsettings.json so every environment has the same defaults. Environment-specific
configuration and environment variables can override them. Startup binds and validates
all four windows, the Premium multiplier, minimum window and at-risk threshold. The
policy snapshots its settings, making the singleton safe from later configuration
object mutation. Changing configuration takes effect after restarting the API.

These are plain domain policies with no EF, HTTP, clock or configuration-framework
dependency. No interface or additional package is needed for these deterministic
calculations. Unit tests use FixedClock for the input instant and cover every priority
and tier, default/forced priority, the minimum window, configurable values and invalid
configuration. TicketBuilder is not needed yet because these tests do not construct
or mutate tickets. The at-risk percentage is validated for the later status step;
this step does not implement SLA status, assignment, ticket creation integration,
escalation, database changes or UI changes.


## Task 2 - Step 3: automatic agent selection

AssignmentPolicy owns eligibility and selection. Eligible candidates are active,
strictly below MaxOpenTickets and specialists when RequiresSpecialist is true.
Candidates are sorted by open workload, then by ID for deterministic ties. No eligible
agent produces an unassigned decision with an explanation, not an exception. The
IsEligible method is reusable when escalation later checks the existing owner.

AgentAssignmentQueries obtains a narrow workload snapshot for a requested category.
SQL counts only tickets outside Resolved and Closed, and uses EXISTS for matching
specialization. One AsNoTracking query handles all agents, avoiding ticket-history
loading and per-agent queries. Inactive/full candidates remain in the snapshot so
the domain policy remains the single owner of eligibility. A specialization match
is specific to the requested category; callers must reload for a different category.

The application abstraction is a database seam with an EF implementation registered
in infrastructure. The domain policy is deterministic and needs no interface or DI
registration. Tests check every eligibility rule, tie ordering, unassigned outcomes
and all five category examples from the PDF. Isolated SQLite tests use FixedClock
and TicketBuilder to verify real workload counts, category matching, one SQL command
and untracked reads. Existing manual-assignment behavior is unchanged.

This step prepares selection but does not assign a ticket or reserve an agent's
capacity. The workload is a read snapshot; Step 4 will integrate the decision with
ticket creation, and escalation will later reuse eligibility. No database migration
or public endpoint is added by this step.


## Task 2 - Step 4: triage on creation

RaiseTicketCommandHandler now loads the customer's tier and category flags, reads fresh
assignment candidates, and calls the existing priority, SLA and assignment policies.
It takes one creation instant from IClock and supplies all decisions to RaiseTriaged,
a named domain factory. The initial row already has its priority, deadline and owner;
one SaveChanges commits the insert. Nobody eligible remains a successful unassigned
creation. Missing customer/category still produces NotFoundException (HTTP 404).

The customer/category repositories now load records instead of returning only an
existence boolean, avoiding an existence check followed by another fetch. The create
response re-reads saved data and adds a typed triage explanation with exactly the
PDF's priorityReason, slaWindowHours, slaReason and assignmentReason fields. The
existing controller keeps its 201 Created response. The TypeScript contract is updated,
while the visible triage result remains part of the later frontend step.

Priority, due date and agent persist in existing columns, so no migration is needed
for this step. Explanation strings describe this operation and are not stored as
ticket columns; normal GET detail responses have null triage. A later SLA-history
step will add timing information needed after escalation.

Tests combine unit checks with the actual seeder, policies, repositories, workload
query and ticket projection in fresh SQLite databases. They verify all five PDF
examples, persistence after clearing EF tracking, cancellation, missing references,
no eligible agent, fresh workloads across consecutive creates and JSON response shape.
No fixtures are inserted into the user's SQL Server database. Assignment remains a
workload snapshot; cross-request capacity locking is not introduced here, and the
starter reference generator still has its original concurrency limitation.


## Task 2 - Steps 5-7: escalation, history and SLA queries

The domain owns allowed priority transitions and captures an immutable before/after
history record before changing the ticket. The use case reuses SlaPolicy and
AssignmentPolicy, keeping an eligible current owner even if another agent has less
work. Otherwise it reassigns with the existing deterministic selection rule. Counts
include the current ticket, so an owner exactly at capacity is ineligible under the
literal strict-below requirement. The injected clock supplies the escalation instant.

History and ticket changes use one EF SaveChanges transaction. Ticket revisions
guard against stale concurrent updates, including status/assignment changes. EF
history guards and a SQL Server UPDATE/DELETE trigger protect the audit trail.
Restricted foreign keys preserve it; (TicketId, EscalatedAtUtc, Id) supports history
lookup and deterministic newest-first ordering. No event-sourcing infrastructure is
needed for this small append-only table.

SLA windows snapshot their UTC start and absolute at-risk cutoff when created or
escalated. Existing deadlines stay fixed if configuration later changes; new windows
use the new configuration after restart. This is consistent with snapshotting the
deadline itself. The status enum is never persisted. Legacy migration backfill uses
the original 25% threshold and preserves existing deadlines; new seeded windows use
the configured percentage.

One SlaEvaluator expression defines status precedence and boundaries. It is compiled
for runtime projection and inlined as expression nodes for SQL filtering/sorting,
without invoking a delegate inside WHERE. Both count and page use the same filtered
query and captured time. Tests check exactly the cutoff, deadline, one tick after,
resolution before/after and relational SQL CASE/count/paging behavior.

The first history query constructed a positional DTO before applying its ID filter;
the relational integration tests exposed that EF could not translate it. Filtering
and sorting now occur on the joined rows before final DTO projection. No in-memory
filtering was introduced. This is an example of using test results to correct an
AI-generated implementation rather than accepting generated code unchecked.

Verification passed 189 backend and 18 frontend tests, lint, typecheck and build.
Two disposable SQL Server databases checked initial migration/seeding and upgrading
a preexisting ticket. Thirty HTTP checks covered validation, missing/conflicting
tickets, persisted history, concurrent requests and SLA querying. Direct SQL audit
changes were rejected by the trigger. Both databases and the temporary API were
removed after verification; only the verified additive migration was applied to the
configured local database. No test tickets were inserted into the user's database.

The actor still comes from the request body until Task 3 supplies authentication.
Step 8's visible controls and component tests remain. Cross-ticket capacity selection
and the starter's generated references retain their original snapshot/concurrency
limitations; per-ticket escalation updates are protected by revisions.
