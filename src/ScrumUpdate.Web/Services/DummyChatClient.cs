using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace ScrumUpdate.Web.Services;

/// <summary>
/// A dummy implementation of IChatClient for testing without Ollama.
/// Generates scrum updates only for explicit commands.
/// </summary>
public class DummyChatClient(IScrumUpdateGenerator scrumUpdateGenerator) : IChatClient
{
    const string GenericResponse = "I am dummy AI and can generate scrum updates on request.";

    static string extractLastUserText(IEnumerable<ChatMessage> chatMessages)
    {
        var lastUserMessage = chatMessages.LastOrDefault(m => m.Role == ChatRole.User);
        return lastUserMessage?.Text ?? string.Empty;
    }

    Task<string> buildAssistantResponseAsync(IEnumerable<ChatMessage> chatMessages, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var userMessage = extractLastUserText(chatMessages);
        var scrumUpdate = scrumUpdateGenerator.TryGenerateScrumUpdateForMessage(userMessage);
        return Task.FromResult(scrumUpdate == null ? GenericResponse : ScrumUpdateResponseFormatter.Format(scrumUpdate));
    }

    void IDisposable.Dispose() { }

    public ChatClientMetadata Metadata => new(nameof(DummyChatClient), new Uri("http://localhost"), "dummy");

    public object? GetService(Type serviceType, object? serviceKey) => this;

    public TService? GetService<TService>(object? key = null) where TService : class => this as TService;

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await buildAssistantResponseAsync(chatMessages, cancellationToken);

        // Simulate streaming by yielding the response character by character
        var delay = 10; // milliseconds between characters

        foreach (var character in response)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            await Task.Delay(delay, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, character.ToString());
        }
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await buildAssistantResponseAsync(chatMessages, cancellationToken);
        return new ChatResponse([new ChatMessage(ChatRole.Assistant, response)]);
    }

}
