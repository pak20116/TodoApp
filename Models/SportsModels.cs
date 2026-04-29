namespace TodoApp.Models;

public record Sport(string Id, string DisplayName);

public record Player(string Id, string FullName, string? Team = null, string? Position = null);

public record PlayerStats(
    string PlayerName,
    string Sport,
    string? Team,
    Dictionary<string, string> Stats
);
