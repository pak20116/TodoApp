using System.Text.Json.Nodes;
using TodoApp.Services.Llm;

namespace TodoApp.Services;

public class SportsQueryService(ILlmProvider provider, ISportsService sportsService) : ISportsQueryService
{
    private const string SystemPrompt = """
        You are an expert sports statistics assistant with access to live data tools.
        Use tools to fetch rosters and player stats before answering.
        Be concise and specific — include actual player names, stats, and scores.
        If you're showing sample/demo data, mention it briefly.
        """;

    private static readonly IReadOnlyList<LlmToolDef> Tools =
    [
        new LlmToolDef(
            "get_sport_players",
            "Get the list of players for a sport. Use this first to find player names and IDs.",
            new
            {
                type = "object",
                properties = new
                {
                    sport = new
                    {
                        type = "string",
                        @enum = new[] { "nhl", "mlb", "nba", "nfl", "epl", "ncaaf", "ncaab", "wwe", "worldcup" },
                        description = "Sport ID"
                    }
                },
                required = new[] { "sport" }
            }
        ),
        new LlmToolDef(
            "get_player_stats",
            "Get statistics for a specific player. Requires player_id from get_sport_players.",
            new
            {
                type = "object",
                properties = new
                {
                    sport       = new { type = "string", description = "Sport ID" },
                    player_id   = new { type = "string", description = "Player ID from get_sport_players" },
                    player_name = new { type = "string", description = "Player's full name" }
                },
                required = new[] { "sport", "player_id", "player_name" }
            }
        )
    ];

    public bool IsConfigured => provider.IsConfigured;

    public async Task<SportsQueryResult> QueryAsync(string question, CancellationToken ct = default)
    {
        if (!provider.IsConfigured)
            return new($"{provider.Name} API key not configured. See appsettings.json → Llm section.", Error: "not_configured");

        var history = new List<LlmTurn> { new UserTurn(question) };
        var toolsUsed = new List<string>();

        for (int round = 0; round < 6; round++)
        {
            var completion = await provider.CompleteAsync(SystemPrompt, history, Tools, ct);

            switch (completion)
            {
                case TextCompletion(var text):
                    return new(text, ToolsUsed: toolsUsed);

                case ErrorCompletion(var error, _):
                    return new(error, Error: error);

                case ToolCallsCompletion(var calls):
                    history.Add(new AssistantTurn(null, calls));
                    var results = new List<LlmToolResult>();
                    foreach (var call in calls)
                    {
                        toolsUsed.Add(call.Name);
                        var result = await ExecuteToolAsync(call.Name, call.Input, ct);
                        results.Add(new LlmToolResult(call.Id, call.Name, result));
                    }
                    history.Add(new ToolResultTurn(results));
                    break;
            }
        }

        return new("Reached maximum reasoning steps without a final answer.", Error: "max_rounds");
    }

    private async Task<string> ExecuteToolAsync(string toolName, JsonNode? input, CancellationToken ct)
    {
        try
        {
            switch (toolName)
            {
                case "get_sport_players":
                {
                    var sport = input?["sport"]?.GetValue<string>() ?? "nba";
                    var players = await sportsService.GetPlayersAsync(sport);
                    var lines = players.Select(p =>
                        $"ID={p.Id} | {p.FullName}" +
                        (p.Team is not null ? $" | {p.Team}" : "") +
                        (p.Position is not null ? $" | {p.Position}" : ""));
                    return $"{sport.ToUpper()} roster ({players.Count} players):\n" + string.Join("\n", lines);
                }
                case "get_player_stats":
                {
                    var sport      = input?["sport"]?.GetValue<string>()       ?? "nba";
                    var playerId   = input?["player_id"]?.GetValue<string>()   ?? "";
                    var playerName = input?["player_name"]?.GetValue<string>() ?? "Unknown";
                    var stats = await sportsService.GetPlayerStatsAsync(sport, playerId, playerName);
                    if (stats is null) return $"No stats found for {playerName}.";
                    var lines = stats.Stats.Select(kv => $"  {kv.Key}: {kv.Value}");
                    return $"{stats.PlayerName} ({stats.Sport}{(stats.Team is not null ? ", " + stats.Team : "")}):\n" + string.Join("\n", lines);
                }
                default:
                    return $"Unknown tool: {toolName}";
            }
        }
        catch (Exception ex) { return $"Tool error: {ex.Message}"; }
    }
}
