using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace ScrumUpdate.Web.Services;

/// <summary>
/// A dummy implementation of IChatClient for testing without Ollama.
/// Returns a simple response indicating it's a dummy AI.
/// </summary>
public class DummyChatClient : IChatClient
{
    const string DummyResponse = "I am dummy AI and don't know how to respond";

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Simulate streaming by yielding the response character by character
        var response = DummyResponse;
        var delay = 50; // milliseconds between characters

        foreach (var character in response)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            await Task.Delay(delay, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, character.ToString());
        }
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, DummyResponse)]));
    }

    public ChatClientMetadata Metadata => new(nameof(DummyChatClient), new Uri("http://localhost"), "dummy");

    public object? GetService(Type serviceType, object? serviceKey) => this;

    public TService? GetService<TService>(object? key = null) where TService : class => this as TService;

    void IDisposable.Dispose() { }
}

