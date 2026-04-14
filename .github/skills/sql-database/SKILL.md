---
name: sql-database
description: 'Query and inspect databases: run SQL queries, view table schemas, check data, diagnose data issues. Use when troubleshooting data problems, verifying migrations, or exploring database contents. Supports SQLite, SQL Server, and PostgreSQL.'
argument-hint: 'Describe what to query or inspect (e.g., "show all users" or "check todos table schema")'
---

# SQL Database Inspector

Query and inspect the application database to troubleshoot data issues, verify schema, and explore contents.

## When to Use

- Verify data after a migration or seed
- Troubleshoot unexpected app behavior by checking actual data
- Inspect table schemas and relationships
- Count records, find duplicates, check constraints

## Procedure

### 1. Determine the Database Provider

Read the connection string from `appsettings.json` (key: `ConnectionStrings:DefaultConnection`):

| Pattern | Provider | CLI Tool |
|---------|----------|----------|
| `DataSource=*.db` | SQLite | `sqlite3` |
| `Server=` or `Data Source=` with port | SQL Server | `sqlcmd` or `Invoke-Sqlcmd` |
| `Host=` | PostgreSQL | `psql` |

### 2. Run Queries

#### SQLite
```powershell
# Schema
sqlite3 "Data/app.db" ".schema TableName"

# Query
sqlite3 -header -column "Data/app.db" "SELECT * FROM TableName LIMIT 20;"

# List tables
sqlite3 "Data/app.db" ".tables"
```

Reference: [SQLite commands](./references/sqlite-commands.md)

#### SQL Server
```powershell
Invoke-Sqlcmd -Query "SELECT * FROM TableName" -ConnectionString "..."
```

Reference: [SQL Server commands](./references/sqlserver-commands.md)

#### PostgreSQL
```powershell
psql "connection_string" -c "SELECT * FROM TableName LIMIT 20;"
```

Reference: [PostgreSQL commands](./references/postgresql-commands.md)

### 3. Common Inspections

- **Table schema**: Show columns, types, and constraints
- **Row counts**: `SELECT COUNT(*) FROM TableName`
- **Recent records**: `SELECT * FROM TableName ORDER BY CreatedAt DESC LIMIT 10`
- **User-scoped data**: Always filter by `UserId` when inspecting user data
- **Migration state**: Check `__EFMigrationsHistory` table

### 4. EF Core Shortcuts

When `dotnet ef` is available, prefer these for schema inspection:

```powershell
# List migrations and their status
dotnet ef migrations list

# Generate SQL for a migration without applying
dotnet ef migrations script --idempotent
```

## Constraints

- DO NOT run `UPDATE`, `DELETE`, or `DROP` without explicit user confirmation
- DO NOT expose connection string credentials in output
- Always use `LIMIT`/`TOP` to avoid dumping entire tables
- For user data, remind the user which `UserId` is being queried
