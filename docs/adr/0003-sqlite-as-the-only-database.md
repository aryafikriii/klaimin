# SQLite as the only database

The app stores everything in one SQLite file through EF Core. There is no database server to install, run, or pay for, and anyone who clones the repo can start the app with one command.

A server database (PostgreSQL or SQL Server) is what a company would run this on. We accepted the gap because the project has a single instance and a handful of concurrent users, which SQLite handles. Moving later means swapping the EF Core provider and regenerating migrations.

## Consequences

Avoid SQLite-only SQL and features that other providers lack, so the provider swap stays small. Money is stored as whole Rupiah in integer columns, which also sidesteps SQLite's lack of a decimal type.
