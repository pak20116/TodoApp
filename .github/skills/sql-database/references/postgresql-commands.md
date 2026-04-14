# PostgreSQL Quick Reference

## psql One-liners
```powershell
# Query
psql "Host=localhost;Database=TodoApp;Username=postgres;Password=..." -c "SELECT * FROM \"Todos\" LIMIT 10;"

# List tables
psql "..." -c "\dt"

# Describe table
psql "..." -c "\d \"Todos\""

# Row counts
psql "..." -c "SELECT COUNT(*) FROM \"Todos\";"
```

## Notes
- PostgreSQL uses double-quoted identifiers for PascalCase table/column names
- EF Core generates PascalCase names by default — always quote them
