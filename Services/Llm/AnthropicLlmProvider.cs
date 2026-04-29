using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TodoApp.Services.Llm;

public class AnthropicLlmProvider(HttpClient http, IConfiguration config) : ILlmProvider
{
    private static readonly JsonSerializerOptions SnakeCase = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly string _apiKey =
        config["Llm:AnthropicApiKey"]
        ?? config["Anthropic:ApiKey"]
        ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
        ?? string.Empty;

    public string Name => "Anthropic Claude";
    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    public async Task<LlmCompletion> CompleteAsync(
        string systemPrompt, IList<LlmTurn> history,
        IReadOnlyList<LlmToolDef> tools, CancellationToken ct)
    {
        var messages = BuildMessages(history);
        var anthropicTools = tools.Select(t => new { name = t.Name, description = t.Description, input_schema = t.InputSchema }).ToArray();

        var requestBody = new
        {
            model = "claude-haiku-4-5-20251001",
            max_tokens = 1024,
            system = systemPrompt,
            tools = anthropicTools,
            messages
        };

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            req.Headers.Add("x-api-key", _apiKey);
            req.Headers.Add("anthropic-version", "2023-06-01");
            req.Content = new StringContent(JsonSerializer.Serialize(requestBody, SnakeCase), Encoding.UTF8, "application/json");

            var resp = await http.SendAsync(req, cts.Token);
            var json = await resp.Content.ReadAsStringAsync(cts.Token);

            if (!resp.IsSuccessStatusCode)
                return new ErrorCompletion($"Anthropic error {resp.StatusCode}: {json}", "api_error");

            return ParseResponse(json);
        }
        catch (OperationCanceledException) { return new ErrorCompletion("Request timed out.", "timeout"); }
        catch (Exception ex) { return new ErrorCompletion($"Error: {ex.Message}", "exception"); }
    }

    private static List<object> BuildMessages(IList<LlmTurn> history)
    {
        var messages = new List<object>();
        foreach (var turn in history)
        {
            switch (turn)
            {
                case UserTurn(var text):
                    messages.Add(new { role = "user", content = text });
                    break;

                case AssistantTurn(var text, var calls):
                    var parts = new List<object>();
                    if (text is not null) parts.Add(new { type = "text", text });
                    if (calls is not null)
                        foreach (var c in calls)
                            parts.Add(new { type = "tool_use", id = c.Id, name = c.Name, input = c.Input ?? JsonNode.Parse("{}") });
                    messages.Add(new { role = "assistant", content = parts });
                    break;

                case ToolResultTurn(var results):
                    var rparts = results.Select(r => (object)new
                    {
                        type = "tool_result", tool_use_id = r.CallId, content = r.Content
                    }).ToArray();
                    messages.Add(new { role = "user", content = rparts });
                    break;
            }
        }
        return messages;
    }

    private static LlmCompletion ParseResponse(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            var stopReason = node?["stop_reason"]?.GetValue<string>();
            var content = node?["content"]?.AsArray();
            if (content is null) return new ErrorCompletion("Unexpected Anthropic response", "parse_error");

            var toolCalls = new List<LlmToolCall>();
            string? textAnswer = null;

            foreach (var block in content)
            {
                var type = block?["type"]?.GetValue<string>();
                if (type == "text")
                    textAnswer = block?["text"]?.GetValue<string>();
                else if (type == "tool_use")
                {
                    var id   = block?["id"]?.GetValue<string>()   ?? Guid.NewGuid().ToString();
                    var name = block?["name"]?.GetValue<string>()  ?? "";
                    toolCalls.Add(new LlmToolCall(id, name, block?["input"]));
                }
            }

            if (stopReason == "tool_use" && toolCalls.Count > 0)
                return new ToolCallsCompletion(toolCalls);

            return new TextCompletion(textAnswer ?? "No answer returned.");
        }
        catch (Exception ex) { return new ErrorCompletion($"Parse error: {ex.Message}", "parse_error"); }
    }
}
