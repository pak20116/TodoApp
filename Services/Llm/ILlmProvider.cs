using System.Text.Json.Nodes;

namespace TodoApp.Services.Llm;

public interface ILlmProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<LlmCompletion> CompleteAsync(
        string systemPrompt,
        IList<LlmTurn> history,
        IReadOnlyList<LlmToolDef> tools,
        CancellationToken ct);
}

// Conversation turns
public abstract record LlmTurn;
public record UserTurn(string Text) : LlmTurn;
public record AssistantTurn(string? Text, IReadOnlyList<LlmToolCall>? ToolCalls = null) : LlmTurn;
public record ToolResultTurn(IReadOnlyList<LlmToolResult> Results) : LlmTurn;

// Tool types
public record LlmToolCall(string Id, string Name, JsonNode? Input);
public record LlmToolResult(string CallId, string ToolName, string Content);
public record LlmToolDef(string Name, string Description, object InputSchema);

// Completion variants
public abstract record LlmCompletion;
public record TextCompletion(string Text) : LlmCompletion;
public record ToolCallsCompletion(IReadOnlyList<LlmToolCall> Calls) : LlmCompletion;
public record ErrorCompletion(string Error, string Code = "") : LlmCompletion;
