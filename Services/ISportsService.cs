using TodoApp.Models;

namespace TodoApp.Services;

public interface ISportsService
{
    List<Sport> GetAvailableSports();
    Task<List<Player>> GetPlayersAsync(string sportId);
    Task<PlayerStats?> GetPlayerStatsAsync(string sportId, string playerId, string playerName);
}
