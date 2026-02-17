using Anthropic;
using Anthropic.Core;
using Google.GenAI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenAI;
using ScrumUpdate.Web.Components;
using ScrumUpdate.Web.Data;
using ScrumUpdate.Web.Services;
using ScrumUpdate.Web.Services.Atlassian;
using System.ClientModel;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Add Entity Framework with in-memory provider
builder.Services.AddDbContext<ChatDbContext>(options =>
    options.UseInMemoryDatabase("ChatDatabase"));

builder.Services.AddScoped<ChatSessionService>();
builder.Services.AddScoped<JiraScrumUpdateDraftService>();
builder.Services.AddScoped<ScrumUpdateTools>();
builder.Services.AddScoped<ICurrentUserContext, HttpCurrentUserContext>();
builder.Services.AddScoped<LocalUserContext>();
builder.Services.AddScoped<AtlassianOAuthService>();
builder.Services.AddHttpContextAccessor();
builder.Services.Configure<AtlassianOAuthOptions>(builder.Configuration.GetSection(AtlassianOAuthOptions.SectionName));
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));
builder.Services.Configure<ClaudeOptions>(builder.Configuration.GetSection(ClaudeOptions.SectionName));
builder.Services.Configure<OpenAIOptions>(builder.Configuration.GetSection(OpenAIOptions.SectionName));

builder.Services.AddHttpClient("WebClient", client => client.Timeout = TimeSpan.FromSeconds(600));
builder.Services.AddHttpClient("AtlassianAuth", client =>
{
    client.BaseAddress = new Uri("https://auth.atlassian.com");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient("AtlassianApi", client =>
{
    client.BaseAddress = new Uri("https://api.atlassian.com");
    client.Timeout = TimeSpan.FromSeconds(30);
});

var scrumGenerator = new ScrumGenerator();
builder.Services.AddSingleton<IScrumUpdateGenerator>(scrumGenerator);
var dummyChatClient = new DummyChatClient(scrumGenerator);
var chatClient = CreateChatClient(builder, dummyChatClient);
builder.Services.AddChatClient(chatClient)
    .UseFunctionInvocation()
    .UseLogging()
    .UseOpenTelemetry(configure: c =>
        c.EnableSensitiveData = builder.Environment.IsDevelopment());

// Applies robust HTTP resilience settings for all HttpClients in the Web project,
// not across the entire solution. It's aimed at supporting Ollama scenarios due
// to its self-hosted nature and potentially slow responses.
// Remove this if you want to use the global or a different HTTP resilience policy instead.
// builder.Services.AddOllamaResilienceHandler();

var app = builder.Build();


// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.UseStaticFiles();
app.MapGet("/auth/atlassian/login", (HttpContext context, AtlassianOAuthService authService, string? returnUrl) =>
{
    return authService.BeginLogin(context, returnUrl);
});

app.MapGet("/auth/atlassian/callback", async (HttpContext context, AtlassianOAuthService authService, string? code, string? state, string? error, string? error_description, CancellationToken cancellationToken) =>
{
    return await authService.HandleCallbackAsync(context, code, state, error, error_description, cancellationToken);
});

app.MapGet("/auth/atlassian/status", async (HttpContext context, AtlassianOAuthService authService, CancellationToken cancellationToken) =>
{
    var status = await authService.GetConnectionStatusAsync(context, cancellationToken);
    return Results.Ok(status);
});

app.MapGet("/auth/atlassian/disconnect", async (HttpContext context, AtlassianOAuthService authService, CancellationToken cancellationToken) =>
{
    return await authService.DisconnectAsync(context, cancellationToken);
});

//app.MapGet("/api/jira/issues", async (HttpContext context, AtlassianOAuthService authService, int? maxResults, CancellationToken cancellationToken) =>
//{
//    try
//    {
//        var issues = await authService.GetMyOpenIssuesAsync(context, maxResults ?? 25, cancellationToken);
//        return Results.Ok(issues);
//    }
//    catch (InvalidOperationException ex)
//    {
//        return Results.BadRequest(new { error = ex.Message });
//    }
//});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static IChatClient CreateChatClient(WebApplicationBuilder builder, DummyChatClient dummyChatClient)
{
    var geminiOptions = builder.Configuration
        .GetSection(GeminiOptions.SectionName)
        .Get<GeminiOptions>() ?? new GeminiOptions();

    if (!string.IsNullOrWhiteSpace(geminiOptions.ApiKey) && !string.IsNullOrWhiteSpace(geminiOptions.Model))
        return new Client(apiKey: geminiOptions.ApiKey).AsIChatClient(defaultModelId: geminiOptions.Model);

    var claudeOptions = builder.Configuration
        .GetSection(ClaudeOptions.SectionName)
        .Get<ClaudeOptions>() ?? new ClaudeOptions();

    if (!string.IsNullOrWhiteSpace(claudeOptions.ApiKey) && !string.IsNullOrWhiteSpace(claudeOptions.Model))
        return new AnthropicClient(new ClientOptions() { ApiKey = claudeOptions.ApiKey })
            .AsIChatClient(claudeOptions.Model);

    var openAIOptions = builder.Configuration
        .GetSection(OpenAIOptions.SectionName)
        .Get<OpenAIOptions>() ?? new OpenAIOptions();

    if (!string.IsNullOrWhiteSpace(openAIOptions.ApiKey) && !string.IsNullOrWhiteSpace(openAIOptions.Model))
    {
        if (string.IsNullOrWhiteSpace(openAIOptions.ApiUrl))
            return new OpenAIClient(openAIOptions.ApiKey)
                .GetChatClient(openAIOptions.Model)
                .AsIChatClient();
        else
        {
            var options = new OpenAIClientOptions
            {
                Endpoint = new Uri(openAIOptions.ApiUrl)
            };

            return new OpenAIClient(new ApiKeyCredential(openAIOptions.ApiKey), options)
                .GetChatClient(openAIOptions.Model)
                .AsIChatClient();
        }
    }

    return dummyChatClient;
}
