/* =====================================================================
   Support Ticket Management System — seed data
   ---------------------------------------------------------------------
   5 categories, 6 customers (3 Premium), 6 agents (1 inactive, 1 at its
   open-ticket limit), 40 tickets spread across every status, priority and
   SLA state.

   All timestamps are computed relative to SYSUTCDATETIME() at run time, so
   the SLA states (within / at risk / breached) stay meaningful whenever the
   script is run. The EF Core development seeder in
   src/Infrastructure/Persistence/SupportDbSeeder.cs inserts exactly this
   data using the same relative offsets.

   Idempotent: it deletes and re-inserts everything.
   ===================================================================== */

USE SupportDesk;
GO

SET NOCOUNT ON;

DELETE FROM dbo.AgentSpecializations;
DELETE FROM dbo.Tickets;
DELETE FROM dbo.Agents;
DELETE FROM dbo.Customers;
DELETE FROM dbo.Categories;
GO

DBCC CHECKIDENT ('dbo.Tickets',    RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.Agents',     RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.Customers',  RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.Categories', RESEED, 0) WITH NO_INFOMSGS;
GO

DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();

/* ------------------------------------------------------------- Categories */
SET IDENTITY_INSERT dbo.Categories ON;
INSERT INTO dbo.Categories (Id, Name, IsActive, RequiresSpecialist, ForcesCriticalPriority) VALUES
    (1, N'General',   1, 0, 0),
    (2, N'Billing',   1, 1, 0),
    (3, N'Technical', 1, 0, 0),
    (4, N'Outage',    1, 1, 1),
    (5, N'Security',  1, 1, 1);
SET IDENTITY_INSERT dbo.Categories OFF;

/* -------------------------------------------------------------- Customers */
/* Tier: 1 = Standard, 2 = Premium */
SET IDENTITY_INSERT dbo.Customers ON;
INSERT INTO dbo.Customers (Id, Name, Email, Phone, Tier, CreatedAtUtc) VALUES
    (1, N'Contoso Ltd',           N'support@contoso.example',      N'+44 20 7946 0101', 2, DATEADD(DAY, -420, @Now)),
    (2, N'Fabrikam Inc',          N'helpdesk@fabrikam.example',    N'+1 415 555 0102',  1, DATEADD(DAY, -365, @Now)),
    (3, N'Adventure Works',       N'it@adventure-works.example',   N'+61 2 5550 0103',  2, DATEADD(DAY, -300, @Now)),
    (4, N'Northwind Traders',     N'ops@northwind.example',        N'+1 206 555 0104',  1, DATEADD(DAY, -260, @Now)),
    (5, N'Tailspin Toys',         N'service@tailspin.example',     N'+353 1 555 0105',  1, DATEADD(DAY, -180, @Now)),
    (6, N'Wide World Importers',  N'support@wideworld.example',    N'+65 6555 0106',    2, DATEADD(DAY, -90,  @Now));
SET IDENTITY_INSERT dbo.Customers OFF;

/* ----------------------------------------------------------------- Agents */
/* Marcus Doyle is deliberately at his MaxOpenTickets limit (6 open tickets),
   so he must be excluded by the "eligible agent" rule.
   Yuki Tanaka is inactive but still owns historic closed tickets.            */
SET IDENTITY_INSERT dbo.Agents ON;
INSERT INTO dbo.Agents (Id, FullName, Email, IsActive, MaxOpenTickets, CreatedAtUtc) VALUES
    (1, N'Alex Turner',    N'alex.turner@northwind.com',    1, 10, DATEADD(DAY, -500, @Now)),
    (2, N'Ben Osei',       N'ben.osei@northwind-support.example',       1,  8, DATEADD(DAY, -460, @Now)),
    (3, N'Priya Nair',     N'priya.nair@northwind-support.example',     1,  8, DATEADD(DAY, -400, @Now)),
    (4, N'Sara Lindqvist', N'sara.lindqvist@northwind-support.example', 1,  6, DATEADD(DAY, -280, @Now)),
    (5, N'Marcus Doyle',   N'marcus.doyle@northwind-support.example',   1,  6, DATEADD(DAY, -200, @Now)),
    (6, N'Yuki Tanaka',    N'yuki.tanaka@northwind-support.example',    0,  8, DATEADD(DAY, -600, @Now));
SET IDENTITY_INSERT dbo.Agents OFF;

/* ---------------------------------------------------- AgentSpecializations */
/*  Agent            General  Billing  Technical  Outage  Security
    1 Alex Turner       x                 x
    2 Ben Osei                            x         x
    3 Priya Nair                                    x        x
    4 Sara Lindqvist    x        x
    5 Marcus Doyle               x                           x
    6 Yuki Tanaka (inactive)               x                          */
INSERT INTO dbo.AgentSpecializations (AgentId, CategoryId) VALUES
    (1, 1), (1, 3),
    (2, 3), (2, 4),
    (3, 4), (3, 5),
    (4, 1), (4, 2),
    (5, 2), (5, 5),
    (6, 3);

/* ---------------------------------------------------------------- Tickets */
/* Columns of the VALUES list:
     Ref, Title, Description, CustomerId, CategoryId, AgentId,
     Priority (1-4), Status (1-5),
     CreatedMinutesAgo, SlaWindowMinutes, ResolvedAfterMinutes (NULL if open)

   SlaWindowMinutes is the response window the team currently works to:
   Critical 4h / High 8h / Medium 24h / Low 72h, halved for Premium customers
   (Contoso, Adventure Works, Wide World Importers).                         */
;WITH Seed (Ref, Title, [Description], CustomerId, CategoryId, AgentId, Priority, [Status], CreatedMinutesAgo, SlaWindowMinutes, ResolvedAfterMinutes) AS
(
    SELECT * FROM (VALUES
    /* ---- open tickets, assigned ------------------------------------------------ */
    (N'TCK-0001', N'Cannot log in to the customer portal',        N'Users get "invalid credentials" even after a password reset. Started after the weekend release.', 1, 3, 1, 3, 3, 1800,  240, NULL),
    (N'TCK-0002', N'Export to CSV produces empty file',           N'The export button downloads a 0 KB file for any date range.',                                   2, 3, 1, 2, 1,  600, 1440, NULL),
    (N'TCK-0003', N'Request for additional user seats',           N'We would like to add five more users to our plan.',                                             4, 1, 1, 1, 2, 6000, 4320, NULL),
    (N'TCK-0004', N'Dashboard charts fail to render in Safari',   N'The charts area stays blank; the console shows a script error.',                                 5, 3, 1, 3, 3,  420,  480, NULL),
    (N'TCK-0005', N'How do I bulk-import contacts?',              N'Documentation link in the app returns a 404.',                                                  6, 1, 1, 2, 2,  180,  720, NULL),
    (N'TCK-0006', N'Production outage - all services unreachable',N'Nothing responds from our region since 09:10 UTC. Entire team is blocked.',                      3, 4, 2, 4, 3,   60,  120, NULL),
    (N'TCK-0007', N'API returns 500 on bulk update',              N'PUT /v1/items/bulk fails for payloads larger than 50 items.',                                   2, 3, 2, 3, 2,  450,  480, NULL),
    (N'TCK-0008', N'Webhook deliveries are delayed',              N'Events arrive up to 20 minutes late since Tuesday.',                                            4, 3, 2, 2, 1,  300, 1440, NULL),
    (N'TCK-0009', N'Suspicious login attempts from unknown IPs',  N'We see repeated failed logins from addresses we do not recognise.',                             1, 5, 3, 4, 2,  105,  120, NULL),
    (N'TCK-0010', N'Regional endpoint intermittently down',       N'Roughly one request in five times out.',                                                        3, 4, 3, 4, 3,   30,  120, NULL),
    (N'TCK-0011', N'Enforce SSO for all our users',               N'We need to block password login now that SSO is live.',                                         6, 5, 3, 3, 2,  200,  240, NULL),
    (N'TCK-0012', N'Scheduled maintenance notice not received',   N'We did not get the maintenance email for last night.',                                          5, 4, 3, 2, 1, 1500, 1440, NULL),
    (N'TCK-0013', N'Invoice total does not match the order',      N'Invoice 5512 is EUR 240 higher than the confirmed order.',                                      2, 2, 4, 3, 3,  200,  480, NULL),
    (N'TCK-0014', N'Update billing address on our account',       N'We moved office last month.',                                                                   4, 2, 4, 1, 1, 1000, 4320, NULL),
    (N'TCK-0015', N'Duplicate charge on September invoice',       N'We were charged twice for the same subscription period.',                                       1, 2, 5, 2, 2,  800,  720, NULL),
    (N'TCK-0016', N'Password reset emails contain a broken link', N'The reset link points to localhost.',                                                           3, 5, 5, 3, 3,   60,  240, NULL),
    (N'TCK-0017', N'VAT number missing from invoices',            N'Our finance team cannot file these invoices.',                                                  5, 2, 5, 2, 2, 1200, 1440, NULL),
    (N'TCK-0018', N'Annual plan quote request',                   N'Please send a quote for switching to annual billing.',                                          6, 2, 5, 1, 1,  600, 2160, NULL),
    (N'TCK-0019', N'Audit log export missing entries',            N'Entries between 02:00 and 04:00 are absent from the export.',                                   4, 5, 5, 3, 2,  240,  480, NULL),
    (N'TCK-0020', N'Timezone shown incorrectly on reports',       N'Reports render in UTC instead of our local timezone.',                                          2, 1, 5, 2, 3,  100, 1440, NULL),
    /* ---- open tickets, unassigned --------------------------------------------- */
    (N'TCK-0021', N'Possible data exposure in shared links',      N'A shared link appears to be accessible without the password.',                                  1, 5, NULL, 4, 1,  45,  120, NULL),
    (N'TCK-0022', N'Credit note not applied to next invoice',     N'The credit note from August was never applied.',                                                4, 2, NULL, 3, 1, 500,  480, NULL),
    (N'TCK-0023', N'Request a walkthrough of the new UI',         N'Could someone show the team the new navigation?',                                               5, 1, NULL, 2, 1,  20, 1440, NULL),
    (N'TCK-0024', N'Mobile app crashes on cold start',            N'Happens on Android 14 only, about half the time.',                                              6, 3, NULL, 1, 1,  90, 2160, NULL),
    /* ---- resolved / closed ---------------------------------------------------- */
    (N'TCK-0025', N'Search returns stale results',                N'Newly created items take minutes to appear in search.',                                         1, 3, 1, 3, 4, 3000,  240,  200),
    (N'TCK-0026', N'Add a second admin to our workspace',         N'Please grant admin rights to our new team lead.',                                               2, 1, 1, 2, 5, 5000, 1440, 1000),
    (N'TCK-0027', N'Checkout unavailable for 40 minutes',         N'Customers could not complete purchases during the incident.',                                   3, 4, 2, 4, 4, 2000,  120,  300),
    (N'TCK-0028', N'Rate limit hit during nightly sync',          N'Our sync job is throttled every night around 01:00.',                                           4, 3, 2, 3, 5, 4000,  480,  400),
    (N'TCK-0029', N'Compromised API key needs rotation',          N'A key was committed to a public repository and must be revoked.',                               1, 5, 3, 4, 4, 1500,  120,   90),
    (N'TCK-0030', N'Two-factor codes rejected intermittently',    N'About one code in three is rejected as invalid.',                                               3, 5, 3, 3, 4, 2500,  240,  600),
    (N'TCK-0031', N'Wrong currency on renewal invoice',           N'Invoice arrived in USD instead of EUR.',                                                         2, 2, 4, 2, 4, 3500, 1440, 1200),
    (N'TCK-0032', N'Refund for cancelled add-on',                 N'The add-on was cancelled but still charged.',                                                   5, 2, 4, 3, 5, 4500,  480,  300),
    (N'TCK-0033', N'Purchase order number missing on invoice',    N'Our AP system rejects invoices without a PO number.',                                           6, 2, 5, 2, 4, 2200,  720,  900),
    (N'TCK-0034', N'Request for onboarding material',             N'Any slides we can share internally?',                                                           4, 1, 5, 1, 5, 6000, 4320, 3000),
    (N'TCK-0035', N'File uploads fail above 20 MB',               N'Uploads stall and then fail without an error message.',                                         2, 3, 6, 2, 5, 5500, 1440, 1100),
    (N'TCK-0036', N'Notifications duplicated three times',        N'Every notification arrives three times.',                                                       5, 3, 6, 3, 4, 3200,  480,  420),
    (N'TCK-0037', N'Database failover caused brief downtime',     N'Roughly six minutes of errors during the failover.',                                            3, 4, 1, 4, 4, 1200,  120,  100),
    (N'TCK-0038', N'Onboarding call follow-up questions',         N'Three questions from our call last week.',                                                      6, 1, 2, 2, 4, 2800,  720,  500),
    (N'TCK-0039', N'Session expires after two minutes',           N'Agents are logged out constantly.',                                                             4, 5, 3, 3, 4, 3300,  480,  700),
    (N'TCK-0040', N'Historic invoices needed for audit',          N'Please provide all invoices for the last two years.',                                           1, 2, 4, 1, 5, 7000, 2160, 1500)
    ) AS v (Ref, Title, [Description], CustomerId, CategoryId, AgentId, Priority, [Status], CreatedMinutesAgo, SlaWindowMinutes, ResolvedAfterMinutes)
)
INSERT INTO dbo.Tickets
    (Reference, Title, [Description], CustomerId, CategoryId, AssignedAgentId, Priority, [Status],
     CreatedAtUtc, UpdatedAtUtc, DueAtUtc, ResolvedAtUtc)
SELECT
    s.Ref,
    s.Title,
    s.[Description],
    s.CustomerId,
    s.CategoryId,
    s.AgentId,
    s.Priority,
    s.[Status],
    DATEADD(MINUTE, -s.CreatedMinutesAgo, @Now),
    DATEADD(MINUTE, -s.CreatedMinutesAgo + ISNULL(s.ResolvedAfterMinutes, s.CreatedMinutesAgo / 2), @Now),
    DATEADD(MINUTE, -s.CreatedMinutesAgo + s.SlaWindowMinutes, @Now),
    CASE WHEN s.ResolvedAfterMinutes IS NULL THEN NULL
         ELSE DATEADD(MINUTE, -s.CreatedMinutesAgo + s.ResolvedAfterMinutes, @Now) END
FROM Seed AS s;
GO

/* =====================================================================
   Verification — run these after seeding. They are also the quickest way
   to sanity-check filter behaviour against the API.
   ===================================================================== */

/* Tickets per priority (1 Low, 2 Medium, 3 High, 4 Critical).
   A "High" filter in the UI must return exactly the Priority = 3 count. */
SELECT Priority, COUNT(*) AS Tickets
FROM dbo.Tickets
GROUP BY Priority
ORDER BY Priority;

/* Open tickets per agent — this is the input to the assignment rule.
   Marcus Doyle (5) should be at his limit of 6 and therefore ineligible. */
SELECT a.Id, a.FullName, a.IsActive, a.MaxOpenTickets,
       COUNT(t.Id) AS OpenTickets
FROM dbo.Agents AS a
LEFT JOIN dbo.Tickets AS t
       ON t.AssignedAgentId = a.Id
      AND t.[Status] NOT IN (4, 5)
GROUP BY a.Id, a.FullName, a.IsActive, a.MaxOpenTickets
ORDER BY a.Id;

/* Derived SLA state distribution. */
SELECT CASE
           WHEN t.DueAtUtc IS NULL THEN 'NotApplicable'
           WHEN t.[Status] IN (4, 5) AND t.ResolvedAtUtc <= t.DueAtUtc THEN 'Met'
           WHEN t.[Status] IN (4, 5) THEN 'Breached'
           WHEN SYSUTCDATETIME() > t.DueAtUtc THEN 'Breached'
           WHEN DATEDIFF(MINUTE, SYSUTCDATETIME(), t.DueAtUtc) * 4
                <= DATEDIFF(MINUTE, t.CreatedAtUtc, t.DueAtUtc) THEN 'AtRisk'
           ELSE 'WithinSla'
       END AS SlaStatus,
       COUNT(*) AS Tickets
FROM dbo.Tickets AS t
GROUP BY CASE
           WHEN t.DueAtUtc IS NULL THEN 'NotApplicable'
           WHEN t.[Status] IN (4, 5) AND t.ResolvedAtUtc <= t.DueAtUtc THEN 'Met'
           WHEN t.[Status] IN (4, 5) THEN 'Breached'
           WHEN SYSUTCDATETIME() > t.DueAtUtc THEN 'Breached'
           WHEN DATEDIFF(MINUTE, SYSUTCDATETIME(), t.DueAtUtc) * 4
                <= DATEDIFF(MINUTE, t.CreatedAtUtc, t.DueAtUtc) THEN 'AtRisk'
           ELSE 'WithinSla'
       END
ORDER BY SlaStatus;

/* Specialist coverage per category. */
SELECT c.Name AS Category, c.RequiresSpecialist,
       COUNT(CASE WHEN a.IsActive = 1 THEN 1 END) AS ActiveSpecialists
FROM dbo.Categories AS c
LEFT JOIN dbo.AgentSpecializations AS s ON s.CategoryId = c.Id
LEFT JOIN dbo.Agents AS a ON a.Id = s.AgentId
GROUP BY c.Name, c.RequiresSpecialist
ORDER BY c.Name;
GO
