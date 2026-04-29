namespace TodoApp.Services;

public interface ISportsQueryService
{
    Task<SportsQueryResult> QueryAsync(string question, CancellationToken ct = default);
    bool IsConfigured { get; }
}

public record SportsQueryResult(string Answer, string? Error = null, IReadOnlyList<string>? ToolsUsed = null);
