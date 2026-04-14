# SQL Server Quick Reference

## PowerShell (Invoke-Sqlcmd)
```powershell
# Query
Invoke-Sqlcmd -Query "SELECT TOP 10 * FROM Todos" -ConnectionString "Server=.;Database=TodoApp;Trusted_Connection=True;TrustServerCertificate=True"

# List tables
Invoke-Sqlcmd -Query "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE'" -ConnectionString "..."

# Column info
Invoke-Sqlcmd -Query "SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Todos'" -ConnectionString "..."

# Row counts
Invoke-Sqlcmd -Query "SELECT COUNT(*) AS Total FROM Todos" -ConnectionString "..."
```

## sqlcmd
```powershell
sqlcmd -S . -d TodoApp -Q "SELECT TOP 10 * FROM Todos" -E
```
