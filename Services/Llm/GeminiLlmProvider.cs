using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TodoApp.Services.Llm;

public class GeminiLlmProvider(HttpClient http, IConfiguration config) : ILlmProvider
{
    private readonly string _apiKey =
        config["Llm:GeminiApiKey"]
        ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
        ?? string.Empty;

    private readonly string _model = config["Llm:GeminiModel"] ?? "gemini-1.5-flash";

    public string Name => "Google Gemini";
    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    public async Task<LlmCompletion> CompleteAsync(
        string systemPrompt, IList<LlmTurn> history,
        IReadOnlyList<LlmToolDef> tools, CancellationToken ct)
    {
        var contents = BuildContents(history);
        var functionDeclarations = tools.Select(t => new { name = t.Name, description = t.Description, parameters = t.InputSchema }).ToArray();

        var requestBody = new
        {
            system_instruction = new { parts = new[] { new { text = systemPrompt } } },
            contents,
            tools = new[] { new { function_declarations = functionDeclarations } },
            tool_config = new { function_calling_config = new { mode = "AUTO" } }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var resp = await http.SendAsync(req, cts.Token);
            var json = await resp.Content.ReadAsStringAsync(cts.Token);

            if (!resp.IsSuccessStatusCode)
                return new ErrorCompletion($"Gemini error {resp.StatusCode}: {json}", "api_error");

            return ParseResponse(json);
        }
        catch (OperationCanceledException) { return new ErrorCompletion("Request timed out.", "timeout"); }
        catch (Exception ex) { return new ErrorCompletion($"Error: {ex.Message}", "exception"); }
    }

    private static List<object> BuildContents(IList<LlmTurn> history)
    {
        var contents = new List<object>();
        foreach (var turn in history)
        {
            switch (turn)
            {
                case UserTurn(var text):
                    contents.Add(new { role = "user", parts = new[] { new { text } } });
                    break;

                case AssistantTurn(var text, var calls):
                    var parts = new List<object>();
                    if (text is not null) parts.Add(new { text });
                    if (calls is not null)
                        foreach (var c in calls)
                            parts.Add(new { functionCall = new { name = c.Name, args = c.Input ?? JsonNode.Parse("{}") } });
                    contents.Add(new { role = "model", parts });
                    break;

                case ToolResultTurn(var results):
                    var rparts = results.Select(r => (object)new
                    {
                        functionResponse = new { name = r.ToolName, response = new { result = r.Content } }
                    }).ToArray();
                    contents.Add(new { role = "user", parts = rparts });
                    break;
            }
        }
        return contents;
    }

    private static LlmCompletion ParseResponse(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            var parts = node?["candidates"]?[0]?["content"]?["parts"]?.AsArray();
            if (parts is null) return new ErrorCompletion("Unexpected Gemini response", "parse_error");

            var toolCalls = new List<LlmToolCall>();
            string? textAnswer = null;

            foreach (var part in parts)
            {
                if (part?["functionCall"] is JsonNode fc)
                {
                    var name = fc["name"]?.GetValue<string>() ?? "";
                    var id = $"gemini_{name}_{Guid.NewGuid():N}";
                    toolCalls.Add(new LlmToolCall(id, name, fc["args"]));
                }
                else if (part?["text"] is JsonNode t)
                {
                    textAnswer = t.GetValue<string>();
                }
            }

            if (toolCalls.Count > 0) return new ToolCallsCompletion(toolCalls);
            return new TextCompletion(textAnswer ?? "No answer returned.");
        }
        catch (Exception ex) { return new ErrorCompletion($"Parse error: {ex.Message}", "parse_error"); }
    }
}
