using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using TodoApp.Models;

namespace TodoApp.Services;

public class SportsService(HttpClient http, IMemoryCache cache, IConfiguration config) : ISportsService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly string _ballDontLieKey  = config["Sports:BallDontLieApiKey"]  ?? "";
    private readonly string _footballDataKey = config["Sports:FootballDataApiKey"] ?? "";

    public List<Sport> GetAvailableSports() =>
    [
        new("nhl",      "NHL Hockey"),
        new("mlb",      "MLB Baseball"),
        new("nba",      "NBA Basketball"),
        new("nfl",      "NFL Football"),
        new("epl",      "Premier League Soccer"),
        new("ncaaf",    "NCAA Football"),
        new("ncaab",    "NCAA Basketball"),
        new("wwe",      "WWE"),
        new("worldcup", "FIFA World Cup"),
    ];

    public async Task<List<Player>> GetPlayersAsync(string sportId)
    {
        var cacheKey = $"players_{sportId}";
        if (cache.TryGetValue(cacheKey, out List<Player>? cached)) return cached!;

        var players = sportId switch
        {
            "mlb"       => await TryFetch(GetMlbPlayersAsync)      ?? GetMockPlayers("mlb"),
            "nhl"       => await TryFetch(GetNhlPlayersAsync)      ?? GetMockPlayers("nhl"),
            "worldcup"  => await TryFetch(GetWorldCupPlayersAsync) ?? GetMockPlayers("worldcup"),
            _           => GetMockPlayers(sportId)
        };

        cache.Set(cacheKey, players, TimeSpan.FromHours(2));
        return players;
    }

    public async Task<PlayerStats?> GetPlayerStatsAsync(string sportId, string playerId, string playerName)
    {
        return sportId switch
        {
            "mlb"      => await TryFetch(() => GetMlbStatsAsync(playerId, playerName))       ?? GetMockStats("mlb", playerName),
            "nhl"      => await TryFetch(() => GetNhlStatsAsync(playerId, playerName))       ?? GetMockStats("nhl", playerName),
            "nba"      => await TryFetch(() => GetNbaStatsAsync(playerName))                 ?? GetMockStats("nba", playerName),
            "worldcup" => await TryFetch(() => GetWorldCupStatsAsync(playerId, playerName))  ?? GetMockStats("worldcup", playerName),
            _          => GetMockStats(sportId, playerName)
        };
    }

    private static async Task<T?> TryFetch<T>(Func<Task<T?>> fetcher) where T : class
    {
        try { return await fetcher(); }
        catch { return null; }
    }

    // ── MLB ───────────────────────────────────────────────────────────────────

    private async Task<List<Player>> GetMlbPlayersAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var json = await http.GetStringAsync(
            "https://statsapi.mlb.com/api/v1/sports/1/players?season=2025", cts.Token);

        using var doc = JsonDocument.Parse(json);
        var players = new List<Player>();

        foreach (var p in doc.RootElement.GetProperty("people").EnumerateArray())
        {
            var id   = p.GetProperty("id").GetInt32().ToString();
            var name = p.TryGetProperty("fullName", out var n) ? n.GetString() : null;
            if (name is null) continue;

            var pos  = p.TryGetProperty("primaryPosition", out var pp) &&
                       pp.TryGetProperty("abbreviation", out var ab) ? ab.GetString() : null;
            var team = p.TryGetProperty("currentTeam", out var ct) &&
                       ct.TryGetProperty("name", out var tn) ? tn.GetString() : null;

            players.Add(new Player(id, name, team, pos));
        }

        return players.OrderBy(p => p.FullName).ToList();
    }

    private async Task<PlayerStats?> GetMlbStatsAsync(string playerId, string playerName)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // Try hitting stats first, then pitching
        foreach (var group in new[] { "hitting", "pitching" })
        {
            var url = $"https://statsapi.mlb.com/api/v1/people/{playerId}/stats?stats=season&season=2025&group={group}";
            var json = await http.GetStringAsync(url, cts.Token);
            using var doc = JsonDocument.Parse(json);

            var splits = doc.RootElement
                .GetProperty("stats").EnumerateArray()
                .FirstOrDefault()
                .TryGetProperty("splits", out var sp) ? sp : default;

            if (splits.ValueKind != JsonValueKind.Array || splits.GetArrayLength() == 0) continue;

            var stat = splits[0].GetProperty("stat");
            var stats = new Dictionary<string, string>();

            // Get team from player info
            string? team = null;
            try
            {
                var infoJson = await http.GetStringAsync(
                    $"https://statsapi.mlb.com/api/v1/people/{playerId}", cts.Token);
                using var infoDoc = JsonDocument.Parse(infoJson);
                var person = infoDoc.RootElement.GetProperty("people")[0];
                if (person.TryGetProperty("currentTeam", out var ct) &&
                    ct.TryGetProperty("name", out var tn))
                    team = tn.GetString();
            }
            catch { }

            if (group == "hitting")
            {
                AddStat(stats, stat, "gamesPlayed",  "Games");
                AddStat(stats, stat, "avg",          "AVG");
                AddStat(stats, stat, "homeRuns",     "HR");
                AddStat(stats, stat, "rbi",          "RBI");
                AddStat(stats, stat, "obp",          "OBP");
                AddStat(stats, stat, "slg",          "SLG");
                AddStat(stats, stat, "ops",          "OPS");
                AddStat(stats, stat, "hits",         "Hits");
                AddStat(stats, stat, "stolenBases",  "SB");
                AddStat(stats, stat, "runs",         "Runs");
                AddStat(stats, stat, "strikeOuts",   "K");
                AddStat(stats, stat, "doubles",      "2B");
            }
            else
            {
                AddStat(stats, stat, "gamesPlayed",  "Games");
                AddStat(stats, stat, "era",          "ERA");
                AddStat(stats, stat, "wins",         "W");
                AddStat(stats, stat, "losses",       "L");
                AddStat(stats, stat, "strikeOuts",   "K");
                AddStat(stats, stat, "whip",         "WHIP");
                AddStat(stats, stat, "inningsPitched","IP");
                AddStat(stats, stat, "hits",         "H Allowed");
                AddStat(stats, stat, "baseOnBalls",  "BB");
                AddStat(stats, stat, "saves",        "SV");
            }

            if (stats.Count > 0)
                return new PlayerStats(playerName, "MLB", team, stats);
        }

        return null;
    }

    // ── NHL ───────────────────────────────────────────────────────────────────

    private async Task<List<Player>> GetNhlPlayersAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var json = await http.GetStringAsync(
            "https://api-web.nhle.com/v1/skater-stats-leaders/now?categories=points&limit=100", cts.Token);

        using var doc = JsonDocument.Parse(json);
        var players = new List<Player>();

        if (!doc.RootElement.TryGetProperty("points", out var list)) return players;

        foreach (var p in list.EnumerateArray())
        {
            var id    = p.GetProperty("id").GetInt64().ToString();
            var first = p.TryGetProperty("firstName", out var fn) &&
                        fn.TryGetProperty("default", out var fd) ? fd.GetString() : "";
            var last  = p.TryGetProperty("lastName", out var ln) &&
                        ln.TryGetProperty("default", out var ld) ? ld.GetString() : "";
            var name  = $"{first} {last}".Trim();
            if (string.IsNullOrEmpty(name)) continue;

            var team = p.TryGetProperty("teamAbbrev", out var ta) &&
                       ta.TryGetProperty("default", out var td) ? td.GetString() : null;
            var pos  = p.TryGetProperty("position", out var po) ? po.GetString() : null;

            players.Add(new Player(id, name, team, pos));
        }

        // Also add goalies
        try
        {
            var gjson = await http.GetStringAsync(
                "https://api-web.nhle.com/v1/goalie-stats-leaders/now?categories=wins&limit=30", cts.Token);
            using var gdoc = JsonDocument.Parse(gjson);
            if (gdoc.RootElement.TryGetProperty("wins", out var glist))
            {
                foreach (var g in glist.EnumerateArray())
                {
                    var id    = g.GetProperty("id").GetInt64().ToString();
                    var first = g.TryGetProperty("firstName", out var fn) &&
                                fn.TryGetProperty("default", out var fd) ? fd.GetString() : "";
                    var last  = g.TryGetProperty("lastName", out var ln) &&
                                ln.TryGetProperty("default", out var ld) ? ld.GetString() : "";
                    var name  = $"{first} {last}".Trim();
                    var team  = g.TryGetProperty("teamAbbrev", out var ta) &&
                                ta.TryGetProperty("default", out var td) ? td.GetString() : null;
                    if (!string.IsNullOrEmpty(name))
                        players.Add(new Player(id, name, team, "G"));
                }
            }
        }
        catch { }

        return players.OrderBy(p => p.FullName).ToList();
    }

    private async Task<PlayerStats?> GetNhlStatsAsync(string playerId, string playerName)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var json = await http.GetStringAsync(
            $"https://api-web.nhle.com/v1/player/{playerId}/landing", cts.Token);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var team = root.TryGetProperty("currentTeamName", out var tn) &&
                   tn.TryGetProperty("default", out var td) ? td.GetString() : null;
        var pos  = root.TryGetProperty("position", out var po) ? po.GetString() : null;

        if (!root.TryGetProperty("featuredStats", out var fs) ||
            !fs.TryGetProperty("regularSeason", out var rs) ||
            !rs.TryGetProperty("subSeason", out var sub))
            return null;

        var isGoalie = pos == "G";
        var stats = new Dictionary<string, string>();

        if (isGoalie)
        {
            AddStat(stats, sub, "gamesPlayed",      "Games");
            AddStat(stats, sub, "wins",             "Wins");
            AddStat(stats, sub, "losses",           "Losses");
            AddStat(stats, sub, "goalsAgainstAvg",  "GAA",    d => $"{d:F2}");
            AddStat(stats, sub, "savePctg",         "SV%",    d => $"{d:F3}");
            AddStat(stats, sub, "shutouts",         "SO");
        }
        else
        {
            AddStat(stats, sub, "gamesPlayed",          "Games");
            AddStat(stats, sub, "goals",                "Goals");
            AddStat(stats, sub, "assists",              "Assists");
            AddStat(stats, sub, "points",               "Points");
            AddStat(stats, sub, "plusMinus",            "+/-");
            AddStat(stats, sub, "pim",                  "PIM");
            AddStat(stats, sub, "shots",                "Shots");
            AddStat(stats, sub, "shootingPctg",         "Shot%",  d => $"{d * 100:F1}%");
            AddStat(stats, sub, "avgToi",               "TOI/G");
            AddStat(stats, sub, "powerPlayGoals",       "PP Goals");
            AddStat(stats, sub, "powerPlayPoints",      "PP Points");
            AddStat(stats, sub, "gameWinningGoals",     "GWG");
        }

        return stats.Count > 0 ? new PlayerStats(playerName, "NHL", team, stats) : null;
    }

    // ── NBA (BallDontLie) ─────────────────────────────────────────────────────

    private async Task<PlayerStats?> GetNbaStatsAsync(string playerName)
    {
        if (string.IsNullOrEmpty(_ballDontLieKey)) return null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // Search by last name to find BallDontLie player ID
        var lastName = playerName.Contains(' ') ? playerName.Split(' ').Last() : playerName;
        using var searchReq = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.balldontlie.io/v1/players?search={Uri.EscapeDataString(lastName)}&per_page=10");
        searchReq.Headers.Add("Authorization", $"Bearer {_ballDontLieKey}");
        var searchResp = await http.SendAsync(searchReq, cts.Token);
        if (!searchResp.IsSuccessStatusCode) return null;

        using var searchDoc = JsonDocument.Parse(await searchResp.Content.ReadAsStringAsync(cts.Token));
        int bdlId = 0;
        string? team = null;
        foreach (var p in searchDoc.RootElement.GetProperty("data").EnumerateArray())
        {
            var fn   = p.TryGetProperty("first_name", out var f) ? f.GetString() ?? "" : "";
            var ln   = p.TryGetProperty("last_name",  out var l) ? l.GetString() ?? "" : "";
            if (!string.Equals($"{fn} {ln}".Trim(), playerName, StringComparison.OrdinalIgnoreCase)) continue;
            bdlId = p.GetProperty("id").GetInt32();
            team  = p.TryGetProperty("team", out var t) && t.TryGetProperty("full_name", out var tn)
                        ? tn.GetString() : null;
            break;
        }
        if (bdlId == 0) return null;

        using var avgReq = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.balldontlie.io/v1/season_averages?season=2024&player_ids[]={bdlId}");
        avgReq.Headers.Add("Authorization", $"Bearer {_ballDontLieKey}");
        var avgResp = await http.SendAsync(avgReq, cts.Token);
        if (!avgResp.IsSuccessStatusCode) return null;

        using var avgDoc = JsonDocument.Parse(await avgResp.Content.ReadAsStringAsync(cts.Token));
        var data = avgDoc.RootElement.GetProperty("data");
        if (data.GetArrayLength() == 0) return null;

        var stat  = data[0];
        var stats = new Dictionary<string, string>();

        if (stat.TryGetProperty("min", out var minEl) && minEl.ValueKind == JsonValueKind.String)
            stats["MPG"] = minEl.GetString() ?? "";

        AddStat(stats, stat, "games_played", "Games");
        AddStat(stats, stat, "pts",          "PPG",  d => $"{d:F1}");
        AddStat(stats, stat, "reb",          "RPG",  d => $"{d:F1}");
        AddStat(stats, stat, "ast",          "APG",  d => $"{d:F1}");
        AddStat(stats, stat, "stl",          "SPG",  d => $"{d:F1}");
        AddStat(stats, stat, "blk",          "BPG",  d => $"{d:F1}");
        AddStat(stats, stat, "fg_pct",       "FG%",  d => $"{d * 100:F1}%");
        AddStat(stats, stat, "fg3_pct",      "3P%",  d => $"{d * 100:F1}%");
        AddStat(stats, stat, "ft_pct",       "FT%",  d => $"{d * 100:F1}%");
        AddStat(stats, stat, "turnover",     "TOV",  d => $"{d:F1}");

        return stats.Count > 0 ? new PlayerStats(playerName, "NBA", team, stats) : null;
    }

    // ── FIFA World Cup (football-data.org) ────────────────────────────────────

    private async Task<List<Player>?> GetWorldCupPlayersAsync()
    {
        if (string.IsNullOrEmpty(_footballDataKey)) return null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        using var req = new HttpRequestMessage(HttpMethod.Get,
            "https://api.football-data.org/v4/competitions/WC/scorers?season=2022&limit=100");
        req.Headers.Add("X-Auth-Token", _footballDataKey);
        var resp = await http.SendAsync(req, cts.Token);
        if (!resp.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cts.Token));
        if (!doc.RootElement.TryGetProperty("scorers", out var scorers)) return null;

        var players = new List<Player>();
        foreach (var s in scorers.EnumerateArray())
        {
            if (!s.TryGetProperty("player", out var p)) continue;
            var id   = p.TryGetProperty("id",   out var pid) ? pid.GetInt32().ToString() : "";
            var name = p.TryGetProperty("name", out var pn)  ? pn.GetString() : null;
            if (string.IsNullOrEmpty(name)) continue;

            var pos  = p.TryGetProperty("position", out var pp) ? pp.GetString() : null;
            var teamName = s.TryGetProperty("team", out var t) && t.TryGetProperty("name", out var tn)
                               ? tn.GetString()
                               : p.TryGetProperty("nationality", out var nat) ? nat.GetString() : null;
            players.Add(new Player(id, name, teamName, pos));
        }
        return players.Count > 0 ? players : null;
    }

    private async Task<PlayerStats?> GetWorldCupStatsAsync(string playerId, string playerName)
    {
        if (string.IsNullOrEmpty(_footballDataKey)) return null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        using var req = new HttpRequestMessage(HttpMethod.Get,
            "https://api.football-data.org/v4/competitions/WC/scorers?season=2022&limit=100");
        req.Headers.Add("X-Auth-Token", _footballDataKey);
        var resp = await http.SendAsync(req, cts.Token);
        if (!resp.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cts.Token));
        if (!doc.RootElement.TryGetProperty("scorers", out var scorers)) return null;

        foreach (var s in scorers.EnumerateArray())
        {
            if (!s.TryGetProperty("player", out var p)) continue;
            var id   = p.TryGetProperty("id",   out var pid) ? pid.GetInt32().ToString() : "";
            var name = p.TryGetProperty("name", out var pn)  ? pn.GetString() : null;
            if (id != playerId && !string.Equals(name, playerName, StringComparison.OrdinalIgnoreCase)) continue;

            var stats = new Dictionary<string, string>();
            if (s.TryGetProperty("goals",        out var g))  stats["Goals"]    = g.GetInt32().ToString();
            if (s.TryGetProperty("assists",       out var a))  stats["Assists"]  = a.GetInt32().ToString();
            if (s.TryGetProperty("penalties",     out var pe)) stats["Pens"]     = pe.GetInt32().ToString();
            if (s.TryGetProperty("playedMatches", out var pm)) stats["Matches"]  = pm.GetInt32().ToString();

            var teamName = s.TryGetProperty("team", out var t) && t.TryGetProperty("name", out var tn)
                               ? tn.GetString()
                               : p.TryGetProperty("nationality", out var nat) ? nat.GetString() : null;

            return stats.Count > 0 ? new PlayerStats(playerName, "FIFA World Cup 2022", teamName, stats) : null;
        }
        return null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void AddStat(Dictionary<string, string> stats, JsonElement el,
        string key, string label, Func<double, string>? format = null)
    {
        if (!el.TryGetProperty(key, out var val)) return;
        string? str = val.ValueKind switch
        {
            JsonValueKind.Number when format is not null => format(val.GetDouble()),
            JsonValueKind.Number => val.ToString(),
            JsonValueKind.String => val.GetString(),
            _ => null
        };
        if (!string.IsNullOrEmpty(str)) stats[label] = str;
    }

    // ── Mock fallbacks ────────────────────────────────────────────────────────

    private static List<Player> GetMockPlayers(string sportId) => sportId switch
    {
        "nhl" =>
        [
            new("8478402", "Connor McDavid",      "Edmonton Oilers",        "C"),
            new("8477934", "Nathan MacKinnon",    "Colorado Avalanche",     "C"),
            new("8478483", "Auston Matthews",     "Toronto Maple Leafs",    "C"),
            new("8476453", "Sidney Crosby",       "Pittsburgh Penguins",    "C"),
            new("8471675", "Alex Ovechkin",       "Washington Capitals",    "LW"),
            new("8480801", "Cale Makar",          "Colorado Avalanche",     "D"),
            new("8481528", "Jack Hughes",         "New Jersey Devils",      "C"),
            new("8477956", "Leon Draisaitl",      "Edmonton Oilers",        "C"),
            new("8475167", "Victor Hedman",       "Tampa Bay Lightning",    "D"),
            new("8767920", "David Pastrnak",      "Boston Bruins",          "RW"),
            new("8479318", "Nikita Kucherov",     "Tampa Bay Lightning",    "RW"),
            new("8474564", "Steven Stamkos",      "Nashville Predators",    "C"),
            new("8475793", "John Tavares",        "Toronto Maple Leafs",    "C"),
            new("8478476", "Matthew Tkachuk",     "Florida Panthers",       "LW"),
            new("8481802", "Brady Tkachuk",       "Ottawa Senators",        "LW"),
            new("8478550", "Mitch Marner",        "Toronto Maple Leafs",    "RW"),
            new("8479339", "William Nylander",    "Toronto Maple Leafs",    "RW"),
            new("8481600", "Elias Pettersson",    "Vancouver Canucks",      "C"),
            new("8481533", "Quinn Hughes",        "Vancouver Canucks",      "D"),
            new("8474600", "Roman Josi",          "Nashville Predators",    "D"),
            new("8481559", "Adam Fox",            "New York Rangers",       "D"),
            new("8479325", "Aleksander Barkov",   "Florida Panthers",       "C"),
            new("8478007", "Brayden Point",       "Tampa Bay Lightning",    "C"),
            new("8481523", "Sam Reinhart",        "Florida Panthers",       "C"),
            new("8479420", "Sebastian Aho",       "Carolina Hurricanes",    "C"),
            new("8480762", "Kyle Connor",         "Winnipeg Jets",          "LW"),
            new("8475786", "Mark Scheifele",      "Winnipeg Jets",          "C"),
            new("8481540", "Jake Guentzel",       "Tampa Bay Lightning",    "LW"),
            new("8479009", "Brock Boeser",        "Vancouver Canucks",      "RW"),
            new("8480052", "Shea Theodore",       "Vegas Golden Knights",   "D"),
        ],
        "mlb" =>
        [
            new("660670", "Shohei Ohtani",          "Los Angeles Dodgers",    "DH/SP"),
            new("592450", "Mike Trout",             "Los Angeles Angels",     "CF"),
            new("665489", "Juan Soto",              "New York Mets",          "RF"),
            new("666185", "Ronald Acuña Jr.",       "Atlanta Braves",         "RF"),
            new("641313", "Mookie Betts",           "Los Angeles Dodgers",    "RF"),
            new("608369", "Freddie Freeman",        "Los Angeles Dodgers",    "1B"),
            new("668939", "Vladimir Guerrero Jr.",  "Toronto Blue Jays",      "1B"),
            new("646240", "Rafael Devers",          "Boston Red Sox",         "3B"),
            new("607208", "Trea Turner",            "Philadelphia Phillies",  "SS"),
            new("665487", "Fernando Tatis Jr.",     "San Diego Padres",       "SS/RF"),
            new("677594", "Julio Rodriguez",        "Seattle Mariners",       "CF"),
            new("670541", "Yordan Alvarez",         "Houston Astros",         "LF/DH"),
            new("663538", "Kyle Tucker",            "Chicago Cubs",           "RF"),
            new("596019", "Corey Seager",           "Texas Rangers",          "SS"),
            new("592518", "Aaron Judge",            "New York Yankees",       "RF"),
            new("543037", "Bryce Harper",           "Philadelphia Phillies",  "1B/DH"),
            new("624413", "Pete Alonso",            "New York Mets",          "1B"),
            new("514888", "José Altuve",            "Houston Astros",         "2B"),
            new("594798", "Marcus Semien",          "Texas Rangers",          "2B"),
            new("668804", "Adley Rutschman",        "Baltimore Orioles",      "C"),
            new("572971", "Gerrit Cole",            "New York Yankees",       "SP"),
            new("434538", "Justin Verlander",       "San Francisco Giants",   "SP"),
            new("554430", "Zack Wheeler",           "Philadelphia Phillies",  "SP"),
            new("669270", "Spencer Strider",        "Atlanta Braves",         "SP"),
            new("671096", "Sandy Alcantara",        "Miami Marlins",          "SP"),
            new("669923", "Corbin Burnes",          "Baltimore Orioles",      "SP"),
            new("543606", "Clayton Kershaw",        "Los Angeles Dodgers",    "SP"),
            new("656427", "Max Fried",              "New York Yankees",       "SP"),
            new("669373", "Logan Webb",             "San Francisco Giants",   "SP"),
            new("808967", "Hyeseong Kim",           "Los Angeles Dodgers",    "SS/2B"),
        ],
        "nba" =>
        [
            new("2544",    "LeBron James",              "Los Angeles Lakers",      "SF"),
            new("201939",  "Stephen Curry",             "Golden State Warriors",   "PG"),
            new("203954",  "Joel Embiid",               "Philadelphia 76ers",      "C"),
            new("1629029", "Luka Doncic",               "Dallas Mavericks",        "PG"),
            new("203507",  "Giannis Antetokounmpo",     "Milwaukee Bucks",         "PF"),
            new("1628369", "Jayson Tatum",              "Boston Celtics",          "SF"),
            new("1629627", "Zion Williamson",           "New Orleans Pelicans",    "PF"),
            new("203999",  "Nikola Jokic",              "Denver Nuggets",          "C"),
            new("203076",  "Anthony Davis",             "Los Angeles Lakers",      "PF/C"),
            new("201142",  "Kevin Durant",              "Phoenix Suns",            "SF"),
            new("1628384", "Shai Gilgeous-Alexander",  "Oklahoma City Thunder",   "PG/SG"),
            new("1628378", "Jaylen Brown",              "Boston Celtics",          "SG/SF"),
            new("1629028", "Trae Young",                "Atlanta Hawks",           "PG"),
            new("1628398", "Donovan Mitchell",         "Cleveland Cavaliers",     "SG"),
            new("1629001", "Jalen Brunson",             "New York Knicks",         "PG"),
            new("1628366", "Devin Booker",              "Phoenix Suns",            "SG"),
            new("1630041", "Anthony Edwards",           "Minnesota Timberwolves",  "SG/SF"),
            new("1631094", "Paolo Banchero",            "Orlando Magic",           "PF"),
            new("1630596", "Evan Mobley",               "Cleveland Cavaliers",     "PF/C"),
            new("1630178", "Tyrese Maxey",              "Philadelphia 76ers",      "PG"),
            new("1628387", "Scottie Barnes",            "Toronto Raptors",         "PF/SF"),
            new("1628386", "Domantas Sabonis",          "Sacramento Kings",        "C"),
            new("1628372", "De'Aaron Fox",              "Sacramento Kings",        "PG"),
            new("1628374", "Jamal Murray",              "Denver Nuggets",          "PG"),
            new("203081",  "Damian Lillard",            "Milwaukee Bucks",         "PG"),
            new("1628423", "Karl-Anthony Towns",        "New York Knicks",         "C/PF"),
            new("1628417", "Pascal Siakam",             "Indiana Pacers",          "PF"),
            new("1630552", "Cade Cunningham",           "Detroit Pistons",         "PG"),
            new("1641705", "Victor Wembanyama",         "San Antonio Spurs",       "C"),
            new("1631167", "Alperen Sengun",            "Houston Rockets",         "C"),
        ],
        "nfl" =>
        [
            new("3915511", "Patrick Mahomes",    "Kansas City Chiefs",      "QB"),
            new("3054211", "Josh Allen",         "Buffalo Bills",           "QB"),
            new("3917315", "Lamar Jackson",      "Baltimore Ravens",        "QB"),
            new("3139477", "Justin Jefferson",   "Minnesota Vikings",       "WR"),
            new("3054245", "Tyreek Hill",        "Miami Dolphins",          "WR"),
            new("3929630", "Ja'Marr Chase",      "Cincinnati Bengals",      "WR"),
            new("4360310", "CeeDee Lamb",        "Dallas Cowboys",          "WR"),
            new("3886807", "Joe Burrow",         "Cincinnati Bengals",      "QB"),
            new("4040715", "Jalen Hurts",        "Philadelphia Eagles",     "QB"),
            new("4426515", "Brock Purdy",        "San Francisco 49ers",     "QB"),
            new("4432577", "Dak Prescott",       "Dallas Cowboys",          "QB"),
            new("3054243", "Derrick Henry",      "Baltimore Ravens",        "RB"),
            new("3054600", "Christian McCaffrey","San Francisco 49ers",     "RB"),
            new("4035538", "Saquon Barkley",     "Philadelphia Eagles",     "RB"),
            new("4040061", "Jonathan Taylor",    "Indianapolis Colts",      "RB"),
            new("3054244", "Travis Kelce",       "Kansas City Chiefs",      "TE"),
            new("3054246", "Mark Andrews",       "Baltimore Ravens",        "TE"),
            new("4047365", "Stefon Diggs",       "Houston Texans",          "WR"),
            new("3915516", "Cooper Kupp",        "Los Angeles Rams",        "WR"),
            new("4035689", "DeVonta Smith",      "Philadelphia Eagles",     "WR"),
            new("4035534", "A.J. Brown",         "Philadelphia Eagles",     "WR"),
            new("4035537", "Garrett Wilson",     "New York Jets",           "WR"),
            new("4360308", "Puka Nacua",         "Los Angeles Rams",        "WR"),
            new("3123913", "Russell Wilson",     "Pittsburgh Steelers",     "QB"),
            new("4239996", "Tua Tagovailoa",     "Miami Dolphins",          "QB"),
            new("4040058", "Justin Herbert",     "Los Angeles Chargers",    "QB"),
            new("4361579", "Drake London",       "Atlanta Falcons",         "WR"),
            new("4380704", "Sam LaPorta",        "Detroit Lions",           "TE"),
            new("4426388", "Bijan Robinson",     "Atlanta Falcons",         "RB"),
            new("4430807", "C.J. Stroud",        "Houston Texans",          "QB"),
        ],
        "epl" =>
        [
            new("44680",  "Erling Haaland",         "Manchester City",     "ST"),
            new("138956", "Mohamed Salah",          "Liverpool",           "RW"),
            new("282679", "Bruno Fernandes",        "Manchester United",   "CAM"),
            new("183518", "Kevin De Bruyne",        "Manchester City",     "CM"),
            new("226597", "Bukayo Saka",            "Arsenal",             "RW"),
            new("341092", "Cole Palmer",            "Chelsea",             "AM"),
            new("209331", "Ollie Watkins",          "Aston Villa",         "ST"),
            new("203574", "Phil Foden",             "Manchester City",     "AM"),
            new("231747", "Marcus Rashford",        "Manchester United",   "LW"),
            new("206567", "Martin Ødegaard",        "Arsenal",             "CAM"),
            new("200145", "Virgil van Dijk",        "Liverpool",           "CB"),
            new("167948", "Trent Alexander-Arnold", "Liverpool",           "RB"),
            new("241323", "Declan Rice",            "Arsenal",             "CM"),
            new("259019", "Rodri",                  "Manchester City",     "DM"),
            new("272462", "Son Heung-min",          "Tottenham",           "LW"),
            new("288539", "James Maddison",         "Tottenham",           "CAM"),
            new("299084", "Dominic Solanke",        "Tottenham",           "ST"),
            new("311082", "Alejandro Garnacho",     "Manchester United",   "LW"),
            new("318574", "Rasmus Højlund",         "Manchester United",   "ST"),
            new("293524", "Leandro Trossard",       "Arsenal",             "LW"),
            new("312679", "Kai Havertz",            "Arsenal",             "ST"),
            new("325021", "Gabriel Martinelli",     "Arsenal",             "LW"),
            new("312554", "William Saliba",         "Arsenal",             "CB"),
            new("305543", "Reece James",            "Chelsea",             "RB"),
            new("312670", "Pedro Neto",             "Chelsea",             "RW"),
            new("318123", "Enzo Fernández",         "Chelsea",             "CM"),
            new("306340", "Jacob Ramsey",           "Aston Villa",         "CM"),
            new("278009", "Alisson Becker",         "Liverpool",           "GK"),
            new("167495", "Ederson",                "Manchester City",     "GK"),
            new("334786", "Lamine Yamal",           "Barcelona",           "RW"),
        ],
        "ncaaf" =>
        [
            new("1",  "Caleb Williams",       "USC Trojans",              "QB"),
            new("2",  "Drake Maye",           "North Carolina",           "QB"),
            new("3",  "Marvin Harrison Jr.",  "Ohio State",               "WR"),
            new("4",  "Rome Odunze",          "Washington Huskies",       "WR"),
            new("5",  "Keon Coleman",         "Florida State",            "WR"),
            new("6",  "Bo Nix",               "Oregon Ducks",             "QB"),
            new("7",  "Michael Penix Jr.",    "Washington Huskies",       "QB"),
            new("8",  "Quinn Ewers",          "Texas Longhorns",          "QB"),
            new("9",  "Carson Beck",          "Georgia Bulldogs",         "QB"),
            new("10", "Dillon Gabriel",       "Oklahoma Sooners",         "QB"),
            new("11", "Will Howard",          "Ohio State",               "QB"),
            new("12", "Ollie Gordon II",      "Oklahoma State",           "RB"),
            new("13", "Blake Corum",          "Michigan Wolverines",      "RB"),
            new("14", "Donovan Edwards",      "Michigan Wolverines",      "RB"),
            new("15", "Jordan Whittington",   "Texas Longhorns",          "WR"),
            new("16", "Emeka Egbuka",         "Ohio State",               "WR"),
            new("17", "Ladd McConkey",        "Georgia Bulldogs",         "WR"),
            new("18", "Brock Bowers",         "Georgia Bulldogs",         "TE"),
            new("19", "Tyleik Williams",      "Ohio State",               "DT"),
            new("20", "Laiatu Latu",          "UCLA Bruins",              "DE"),
            new("21", "Dallas Turner",        "Alabama Crimson Tide",     "DE"),
            new("22", "Chop Robinson",        "Penn State",               "DE"),
            new("23", "Jer'Zhan Newton",      "Illinois Fighting Illini", "DT"),
            new("24", "Kool-Aid McKinstry",   "Alabama Crimson Tide",     "CB"),
            new("25", "Terrion Arnold",       "Alabama Crimson Tide",     "CB"),
            new("26", "Kamren Kinchens",      "Miami Hurricanes",         "S"),
            new("27", "Tyler Nubin",          "Minnesota Gophers",        "S"),
            new("28", "Caden Stover",         "Ohio State",               "TE"),
            new("29", "Evan Stewart",         "Oregon Ducks",             "WR"),
            new("30", "Isaiah Bond",          "Texas Longhorns",          "WR"),
        ],
        "ncaab" =>
        [
            new("1",  "Zach Edey",             "Purdue Boilermakers",     "C"),
            new("2",  "Reed Sheppard",         "Kentucky Wildcats",       "G"),
            new("3",  "Rob Dillingham",        "Kentucky Wildcats",       "G"),
            new("4",  "Dalton Knecht",         "Tennessee Volunteers",    "G/F"),
            new("5",  "Donovan Clingan",       "UConn Huskies",           "C"),
            new("6",  "Kyle Filipowski",       "Duke Blue Devils",        "C/F"),
            new("7",  "Stephon Castle",        "UConn Huskies",           "G"),
            new("8",  "Isaiah Collier",        "USC Trojans",             "G"),
            new("9",  "Matas Buzelis",         "G League Ignite",         "F"),
            new("10", "Jared McCain",          "Duke Blue Devils",        "G"),
            new("11", "D.J. Wagner",           "Kentucky Wildcats",       "G"),
            new("12", "Justin Edwards",        "Kentucky Wildcats",       "G/F"),
            new("13", "Payton Sandfort",       "Iowa Hawkeyes",           "F"),
            new("14", "Dillon Mitchell",       "Texas Longhorns",         "F"),
            new("15", "Adem Bona",             "UCLA Bruins",             "C"),
            new("16", "Johnny Furphy",         "Kansas Jayhawks",         "F"),
            new("17", "Hunter Dickinson",      "Kansas Jayhawks",         "C"),
            new("18", "RJ Davis",              "North Carolina",          "G"),
            new("19", "Armando Bacot",         "North Carolina",          "C"),
            new("20", "Tyrese Proctor",        "Duke Blue Devils",        "G"),
            new("21", "Mark Sears",            "Alabama Crimson Tide",    "G"),
            new("22", "Johni Broome",          "Auburn Tigers",           "C/F"),
            new("23", "Devin Carter",          "Providence Friars",       "G"),
            new("24", "Boogie Fland",          "Arkansas Razorbacks",     "G"),
            new("25", "Tre Johnson",           "Texas Longhorns",         "G"),
            new("26", "Cooper Flagg",          "Duke Blue Devils",        "F"),
            new("27", "Dylan Harper",          "Rutgers Scarlet Knights", "G"),
            new("28", "Ace Bailey",            "Rutgers Scarlet Knights", "F"),
            new("29", "VJ Edgecombe",          "Baylor Bears",            "G"),
            new("30", "Kon Knueppel",          "Duke Blue Devils",        "G/F"),
        ],
        "wwe" =>
        [
            new("1",  "Cody Rhodes",       "Raw",        "Champion"),
            new("2",  "Roman Reigns",      "SmackDown",  "Tribal Chief"),
            new("3",  "Gunther",           "Raw",        "IC Champion"),
            new("4",  "CM Punk",           "Raw",        "Superstar"),
            new("5",  "Seth Rollins",      "Raw",        "Superstar"),
            new("6",  "Rhea Ripley",       "Raw",        "Women's Champion"),
            new("7",  "Becky Lynch",       "SmackDown",  "Superstar"),
            new("8",  "Sami Zayn",         "SmackDown",  "Superstar"),
            new("9",  "Kevin Owens",       "SmackDown",  "Superstar"),
            new("10", "Drew McIntyre",     "Raw",        "Superstar"),
            new("11", "Randy Orton",       "SmackDown",  "Legend Killer"),
            new("12", "Rey Mysterio",      "SmackDown",  "Superstar"),
            new("13", "Dominik Mysterio",  "Raw",        "Superstar"),
            new("14", "Finn Bálor",        "Raw",        "The Prince"),
            new("15", "Damian Priest",     "Raw",        "Superstar"),
            new("16", "Bianca Belair",     "SmackDown",  "Superstar"),
            new("17", "Charlotte Flair",   "SmackDown",  "Superstar"),
            new("18", "Asuka",             "Raw",        "Superstar"),
            new("19", "Bayley",            "SmackDown",  "Role Model"),
            new("20", "LA Knight",         "SmackDown",  "Mega Star"),
            new("21", "Logan Paul",        "Raw",        "Influencer"),
            new("22", "The Miz",           "Raw",        "A-Lister"),
            new("23", "Chad Gable",        "Raw",        "Superstar"),
            new("24", "Bronson Reed",      "Raw",        "Superstar"),
            new("25", "Karrion Kross",     "SmackDown",  "Superstar"),
            new("26", "Santos Escobar",    "SmackDown",  "Superstar"),
            new("27", "Dragon Lee",        "Raw",        "Superstar"),
            new("28", "Ricochet",          "SmackDown",  "Superstar"),
            new("29", "Jey Uso",           "Raw",        "Main Event"),
            new("30", "Ivar",              "Raw",        "Viking"),
        ],
        "worldcup" =>
        [
            new("1",  "Lionel Messi",         "Argentina",  "FW"),
            new("2",  "Kylian Mbappé",        "France",     "FW"),
            new("3",  "Cristiano Ronaldo",    "Portugal",   "FW"),
            new("4",  "Neymar Jr.",           "Brazil",     "FW"),
            new("5",  "Lamine Yamal",         "Spain",      "RW"),
            new("6",  "Vinicius Jr.",         "Brazil",     "LW"),
            new("7",  "Erling Haaland",       "Norway",     "ST"),
            new("8",  "Jude Bellingham",      "England",    "CAM"),
            new("9",  "Harry Kane",           "England",    "ST"),
            new("10", "Pedri",                "Spain",      "CM"),
            new("11", "Florian Wirtz",        "Germany",    "AM"),
            new("12", "Jamal Musiala",        "Germany",    "AM"),
            new("13", "Gavi",                 "Spain",      "CM"),
            new("14", "Rodri",                "Spain",      "DM"),
            new("15", "Dani Olmo",            "Spain",      "AM"),
            new("16", "Bukayo Saka",          "England",    "RW"),
            new("17", "Phil Foden",           "England",    "AM"),
            new("18", "Antoine Griezmann",    "France",     "AM"),
            new("19", "Ousmane Dembélé",      "France",     "RW"),
            new("20", "Marcus Rashford",      "England",    "LW"),
            new("21", "Richarlison",          "Brazil",     "ST"),
            new("22", "Lucas Paquetá",        "Brazil",     "CAM"),
            new("23", "Achraf Hakimi",        "Morocco",    "RB"),
            new("24", "Sofiane Boufal",       "Morocco",    "LW"),
            new("25", "Mohamed Salah",        "Egypt",      "RW"),
            new("26", "Victor Osimhen",       "Nigeria",    "ST"),
            new("27", "Sadio Mané",           "Senegal",    "FW"),
            new("28", "Bernardo Silva",       "Portugal",   "CM"),
            new("29", "Diogo Jota",           "Portugal",   "FW"),
            new("30", "Alvaro Morata",        "Spain",      "ST"),
        ],
        _ => []
    };

    private static PlayerStats GetMockStats(string sportId, string playerName) => sportId switch
    {
        "nhl" => new(playerName, "NHL", null, new()
        {
            ["Games"] = "82", ["Goals"] = "52", ["Assists"] = "86",
            ["Points"] = "138", ["+/-"] = "+28", ["PIM"] = "24",
            ["Shots"] = "280", ["Shot%"] = "18.6%", ["TOI/G"] = "22:14",
            ["PP Goals"] = "18", ["GWG"] = "9"
        }),
        "mlb" => new(playerName, "MLB", null, new()
        {
            ["Games"] = "159", ["AVG"] = ".310", ["HR"] = "44",
            ["RBI"] = "130", ["OBP"] = ".412", ["SLG"] = ".654",
            ["OPS"] = "1.066", ["SB"] = "59", ["Hits"] = "180", ["K"] = "98"
        }),
        "nba" => new(playerName, "NBA", null, new()
        {
            ["Games"] = "71", ["PPG"] = "27.1", ["RPG"] = "7.5",
            ["APG"] = "8.3", ["FG%"] = "52.4%", ["3P%"] = "40.8%",
            ["FT%"] = "91.5%", ["SPG"] = "1.3", ["BPG"] = "0.6"
        }),
        "nfl" => new(playerName, "NFL", null, new()
        {
            ["Games"] = "17", ["Pass Yds"] = "5,250", ["TDs"] = "50",
            ["INTs"] = "14", ["Comp%"] = "67.1%", ["Rating"] = "113.8",
            ["Rush Yds"] = "531", ["Rush TDs"] = "4"
        }),
        "epl" => new(playerName, "EPL", null, new()
        {
            ["Apps"] = "35", ["Goals"] = "36", ["Assists"] = "8",
            ["Shots/Game"] = "5.2", ["Pass%"] = "83.2%", ["Rating"] = "8.72",
            ["Key Passes"] = "2.8"
        }),
        "ncaaf" => new(playerName, "NCAA Football", null, new()
        {
            ["Games"] = "13", ["Pass Yds"] = "4,123", ["TDs"] = "38",
            ["INTs"] = "5", ["Comp%"] = "71.2%", ["Rating"] = "177.4"
        }),
        "ncaab" => new(playerName, "NCAA Basketball", null, new()
        {
            ["Games"] = "36", ["PPG"] = "22.3", ["RPG"] = "9.1",
            ["APG"] = "2.4", ["FG%"] = "60.1%", ["BPG"] = "2.1"
        }),
        "wwe" => new(playerName, "WWE", null, new()
        {
            ["Championships"] = "3", ["Title Reigns"] = "5",
            ["WrestleMania"] = "4", ["Royal Rumble"] = "1",
            ["Money in the Bank"] = "2"
        }),
        "worldcup" => new(playerName, "FIFA World Cup", null, new()
        {
            ["Tournaments"] = "5", ["Goals"] = "14", ["Assists"] = "8",
            ["Appearances"] = "26", ["Wins"] = "17", ["Man of Match"] = "6"
        }),
        _ => new(playerName, sportId.ToUpper(), null, new()
        {
            ["Stat 1"] = "N/A", ["Stat 2"] = "N/A"
        })
    };
}
