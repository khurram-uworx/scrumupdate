using Microsoft.Extensions.AI;
using ScrumUpdate.Web.Services;

namespace ScrumUpdate.Tests;

[TestFixture]
public class DummyChatClientTests
{
    [Test]
    public async Task GetResponseAsync_NonScrumMessage_ReturnsGenericChatResponse()
    {
        var generator = new ScrumGenerator();
        var client = new DummyChatClient(generator);

        var response = await client.GetResponseAsync(
        [
            new ChatMessage(ChatRole.User, "hi")
        ]);

        Assert.That(response.Messages.Single().Text, Is.EqualTo("I am dummy AI and can generate scrum updates on request."));
    }

    [Test]
    public async Task GetResponseAsync_ScrumUpdateAndRegenerate_ReturnDifferentScrumMessages()
    {
        var generator = new ScrumGenerator();
        var client = new DummyChatClient(generator);

        var first = await client.GetResponseAsync(
        [
            new ChatMessage(ChatRole.User, "scrum update")
        ]);

        var second = await client.GetResponseAsync(
        [
            new ChatMessage(ChatRole.User, "regenerate")
        ]);

        var firstText = first.Messages.Single().Text;
        var secondText = second.Messages.Single().Text;

        Assert.That(firstText, Does.StartWith("Scrum update for "));
        Assert.That(secondText, Does.StartWith("Scrum update for "));
        Assert.That(secondText, Is.Not.EqualTo(firstText));
    }

    [Test]
    public void TryGenerateScrumUpdateForMessage_OnlyGeneratesForExpectedCommands()
    {
        var generator = new ScrumGenerator();

        var none = generator.TryGenerateScrumUpdateForMessage("hey");
        var scrum = generator.TryGenerateScrumUpdateForMessage("scrum update");
        var regenerated = generator.TryGenerateScrumUpdateForMessage("regenerate");

        Assert.That(none, Is.Null);
        Assert.That(scrum, Is.Not.Null);
        Assert.That(regenerated, Is.Not.Null);
        Assert.That(regenerated!.WhatIDidYesterday, Is.Not.EqualTo(scrum!.WhatIDidYesterday));
    }

    [Test]
    public async Task TryParseGeneratedScrumUpdateFromAssistantMessage_ParsesOnlyScrumResponses()
    {
        var generator = new ScrumGenerator();
        var client = new DummyChatClient(generator);
        var generic = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);
        var scrum = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "scrum update")]);

        var genericParsed = generator.TryParseGeneratedScrumUpdateFromAssistantMessage(generic.Messages.Single().Text ?? string.Empty);
        var scrumParsed = generator.TryParseGeneratedScrumUpdateFromAssistantMessage(scrum.Messages.Single().Text ?? string.Empty);

        Assert.That(genericParsed, Is.Null);
        Assert.That(scrumParsed, Is.Not.Null);
    }
}
