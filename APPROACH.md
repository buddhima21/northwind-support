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
Reasons are validated; Task 3 now supplies the actor from the authenticated agent.
The current ticket counts toward
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
limitation. Task 3 now supplies the escalation actor from the token identity. The optional
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

## Task 3, step 2: login and signed identity

The application defines the authentication contract and DTOs; Infrastructure implements
credential lookup, Identity password verification and JWT issuance. The HTTP controller
only maps success/failure to responses and reads the authenticated subject for `/me`.
This keeps EF and signing libraries out of Application and Domain. I used the built-in
bearer middleware rather than writing token parsing or signature validation myself.

Email matching trims and compares case-insensitively; the six-agent scope makes the
SQL UPPER comparison acceptable, while a large user directory should use an indexed
normalized email column. Passwords are never trimmed. Failed authentication has one
generic message, with dummy password verification for unknown/missing credentials.
Inactive users are rejected even with the correct password. Successful login rehashes
credentials if Identity requests an algorithm/work-factor upgrade.

Tokens last one hour and contain a stable agent ID in `sub`, public profile claims,
and a unique identifier. Issuance uses IClock; middleware validates signature, issuer,
audience, HS256 algorithm and expiry without clock-skew grace. The signing key comes
from validated configuration. The committed development key is a public fixture only.
`/me` reloads the active agent so stale profile claims do not override current data.

HTTP integration tests use the real middleware and controllers with isolated SQLite
databases and the existing FixedClock. They exposed an early configuration-binding
problem: test-host overrides were applied after Program first read JWT settings. I
moved resolution to the built host and retained startup validation before requests.
Global authorization and the escalation actor were implemented in the following step.

Step 2 verification passed all 14 HTTP tests, 188 existing backend tests and four
architecture tests. Test hosting also required resolving the database connection string
when configuring DbContext rather than before the host could replace its provider.
No database migration was needed in this step; the credential column is from Step 1.

## Task 3, step 3: central protection and authenticated history

I used a fallback authorization policy so future endpoints are protected automatically.
Login opts out explicitly; Development Swagger stays ahead of authorization in the
middleware pipeline. Bearer validation also checks the subject against active agents,
so a deactivated or deleted agent cannot continue using an otherwise valid token.
This costs a small lookup per request, which is appropriate for this six-agent app.

The escalation HTTP DTO now contains only the reason. The controller obtains the
validated subject and passes the ID as a separate handler argument; the application
therefore has no HttpContext or claims dependency. A current-user service abstraction
would add another seam where an ordinary trusted argument is sufficient. The handler
records agent:<id>, a stable identifier that fits the existing history column even
when names or emails are long. Old history remains intact without a new migration.

I removed the editable actor field from the form and its payload type in the same
commit so the client contract matches the server. Obsolete caller-actor validation
tests were replaced with authenticated HTTP behavior tests. The tests include forged
JSON actor fields for two distinct logins, persisted history, all current endpoint
methods, and a new unannotated test endpoint proving the fallback policy applies.
The browser login/session flow was added in Step 4. Protected API access can also be
checked with the documented PowerShell commands.

Verification passed 210 backend tests (184 unit/integration, four architecture and
22 HTTP), 43 frontend tests, web lint, TypeScript checking and the production build.
Obsolete actor-input validation cases account for the lower unit/frontend counts;
the new HTTP cases verify actor provenance and central protection directly.

## Task 3, step 4: login, routes and session lifecycle

I extended the existing API client to attach tokens and invalidate the matching session
on 401. Authentication endpoints use that client too, with login explicitly anonymous.
A small subscribed session store is shared between React and the client, avoiding
headers in components and avoiding a dependency from networking code on React Router.
React reads the store with useSyncExternalStore; a single route guard controls the
application shell and all ticket/customer pages, including unknown routes.

I chose sessionStorage to preserve refreshes within a tab while limiting persistence.
Only token, expiry and profile are saved; passwords never enter storage. This is not
XSS-resistant, and logout does not revoke an issued JWT. I documented HttpOnly-cookie
sessions with CSRF protection as a production alternative rather than changing the
assignment's bearer-token contract or adding refresh/revocation infrastructure.

Persisted credentials start in a restoring state. `/me` must succeed before protected
pages mount, preventing stale profile data or an invalid token from exposing the UI.
Transient server/network failures preserve the session and offer retry; 401 clears it.
Timers handle expiry while mounted, and a focus check catches browser timer throttling.
Restoration and 401 handlers compare token identity so an earlier request cannot erase
or overwrite a later login. Logout removes credentials, unmounts protected content and
returns to login. Destination validation restricts redirects to local non-login paths.

The login screen reuses the existing Field component and styling, with labelled inputs,
password-manager hints, matching input limits, pending/duplicate-submit protection and
generic server rejection messages. No registration, reset-password, role management
or new state-management dependency was added. Tests use the actual route tree under
StrictMode, the actual API client with mocked HTTP, and saved-session restoration.
Headless Edge checks of the production build verify desktop/mobile layouts, login,
refresh, bearer attachment, 401 redirects and logout using deterministic mock responses.

Verification passed all 77 frontend tests, lint, TypeScript checking and the production
build. Desktop/mobile screenshots were reviewed; headless Edge reported no page errors.
The browser checks used mocked HTTP and did not modify the local SQL Server database.

## Task 3, step 5: final review and real-stack verification

I compared the final branch with AU-1 through AU-13 and the PDF's definition of done.
All thirteen requirements were satisfied. All 287 automated tests,
lint, type checking, the root build and the EF migration-model check passed. The only
dependency warnings are the existing transitive Pkcs warnings.

Earlier frontend browser checks used mocked HTTP. Final verification added a separate
real API and web server connected to a disposable SQL Server database. Fresh startup
applied the migrations and provisioned six separately salted hashes. Live HTTP checks
covered authentication, Swagger, CORS, protected endpoints, filtering, triaged creation,
authenticated escalation and persistent history. Headless Edge then exercised the real
login/escalation/refresh/401/logout flow at desktop and mobile sizes.

To check the user's actual upgrade path without modifying their database, I reverted
only the credential migration in that disposable database, retaining 41 tickets and
two history rows. Startup upgraded the schema again and seeded six missing hashes;
counts, login and history were preserved. The temporary database/servers were removed.
No additional application-code change was needed after the final review.

I replaced the growing per-step README history with a concise reviewer guide, fixed
the assignment link and retained detailed reasoning here. The PR description is
provided separately for copying into GitHub rather than stored in the repository.
Actual candidate time spent remains for the candidate to supply; no estimate was
invented. The implementation commits are preserved, and merging remains a separate
review action after this final documentation commit.

## Development credential update

Alex's development login is now `alex.turner@northwind.com` / `northwind_alexturner`.
Fresh databases hash that password; the existing development seeder upgrades the old
Alex fixture in place, preserving his ID and relationships. It also handles an email
already changed while the old shared fixture password is still present. Subsequent
runs preserve the new hash. Other agents retain their credentials, and an independently
changed password on the new Alex account is preserved. These are public local fixtures,
not production credentials. Regression tests cover both existing-account upgrade paths.

The separate review-checklist and PR-description files were removed at the candidate's
request. This document and README remain the repository's handover documentation.

Verification after the credential change passed 186 backend unit/integration tests,
four architecture tests, 22 HTTP tests and 77 frontend tests (289 total), including
both existing-Alex upgrade cases and preservation of independently changed passwords.
