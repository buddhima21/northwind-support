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




