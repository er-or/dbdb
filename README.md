# dbdb
Cross-engine SQL client for MySQL, SQL Server, and SQLite - REPL, saved sessions, mysqldump-style export.

**Standalone executable - no install required.** `dbdb.exe` is fully self-contained: no .NET runtime, no MySQL client, no `sqlcmd`, no SQLite tools needed on the target machine. Copy the exe over and run it. This was the whole point of building it - being able to query a database from a locked-down machine where you can't easily install `mysql`, `sqlcmd`, or any other client tooling.
