# Implementation approach

## Task 1  Defect fix and server-side filtering

DEFECT-117 was a missing-request bug: the fetch effect omitted dropdown filters from its dependencies. I changed it to observe the complete filter object, debounce only search and reject stale responses. The regression test inspects the outgoing request and failed against the original code. Reloading the page or filtering displayed rows would have hidden the cause, so those approaches were rejected.

Filtering stays in `TicketQueries.ApplyFilters`, before COUNT and pagination. Exact matches, search and SLA predicates share this path, making another filter a local extension. Relational tests inspect generated SQL; live SQL Server logs confirm filtering occurs in the database. No new query framework was needed.

## Task 2  Triage, assignment and escalation

Shared domain policies decide priority, SLA and assignment for both creation and escalation. Configuration supplies SLA values; category flags control forced priority and specialisation. Assignment uses active agents below capacity, fewest open tickets and an ID tie-break. An unassigned ticket is valid when nobody qualifies. This keeps business rules out of controllers and avoids duplicated calculations.

Escalation validates eligibility, recalculates the deadline and saves the ticket with immutable history in one transaction. Revision concurrency rejects stale writes, and a composite index supports history retrieval. The frontend shares an SLA indicator and handles validation, conflicts and refresh without page reloads. Fixed-clock, relational and browser checks cover the rules; cross-ticket capacity races remain a documented limitation.

## Task 3  Authentication and session handling

Agent is the existing user identity, so I added a nullable password hash rather than another user table. Identity's password hasher and JWT middleware handle hashing and validation. The fallback authorization policy protects business endpoints centrally, while escalation receives the validated agent ID separately from its request body. Development seeding supports both fresh and existing databases without resetting established credentials.

The frontend shares one token-aware client and session store. sessionStorage preserves refreshes within a tab; `/me`, expiry and 401 handling validate or clear the session. HTTP and frontend tests cover generic login failures, forged actors, protected routes and session races; real browser/API checks cover login through logout. XSS exposure and lack of token revocation are documented production trade-offs.

## Task 4  API containerisation

The root-context Dockerfile restores dependency metadata before copying source, then publishes into a smaller ASP.NET runtime running as `app` on HTTP 8080. Ignore rules exclude local credentials and host outputs. Compose publishes localhost:5080, injects settings at runtime and waits for healthy SQL. Existing Development startup handles migrations/seeding; production should use a controlled migration job instead.

An anonymous database-aware `/health` returns 200 or 503 without weakening business endpoint authentication. Verification used a separate empty volume: three migrations, 40 seeded tickets, successful login, restart persistence and frontend proxy checks passed. Restore caching and non-root execution were verified; the runtime image measured 433.8 MiB against the 1243.4 MiB SDK baseline. Temporary resources were removed without deleting the existing development database.
