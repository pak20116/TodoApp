# SQLite Quick Reference

## Interactive Mode
```powershell
sqlite3 "Data/app.db"
```

## Common Dot Commands
| Command | Purpose |
|---------|---------|
| `.tables` | List all tables |
| `.schema TableName` | Show CREATE statement |
| `.headers on` | Show column headers |
| `.mode column` | Columnar output |
| `.quit` | Exit |

## One-liner Queries
```powershell
# List tables
sqlite3 "Data/app.db" ".tables"

# Show schema
sqlite3 "Data/app.db" ".schema Todos"

# Query with headers
sqlite3 -header -column "Data/app.db" "SELECT * FROM Todos LIMIT 10;"

# Count rows
sqlite3 "Data/app.db" "SELECT COUNT(*) FROM Todos;"

# Check migrations
sqlite3 -header -column "Data/app.db" "SELECT * FROM __EFMigrationsHistory;"

# Describe columns (pragma)
sqlite3 -header -column "Data/app.db" "PRAGMA table_info('Todos');"

# Foreign keys
sqlite3 -header -column "Data/app.db" "PRAGMA foreign_key_list('Todos');"

# Indexes
sqlite3 -header -column "Data/app.db" "PRAGMA index_list('Todos');"
```
