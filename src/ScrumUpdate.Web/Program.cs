using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using ScrumUpdate.Web.Components;
using ScrumUpdate.Web.Data;
using ScrumUpdate.Web.Services;
using ScrumUpdate.Web.Services.Atlassian;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Add Entity Framework with in-memory provider
builder.Services.AddDbContext<ChatDbContext>(options =>
    options.UseInMemoryDatabase("ChatDatabase"));

builder.Services.AddScoped<ChatSessionService>();
builder.Services.AddScoped<ICurrentUserContext, HttpCurrentUserContext>();
builder.Services.AddScoped<LocalUserContext>();
builder.Services.AddScoped<AtlassianOAuthService>();
builder.Services.AddHttpContextAccessor();
builder.Services.Configure<AtlassianOAuthOptions>(builder.Configuration.GetSection(AtlassianOAuthOptions.SectionName));

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

// Use the dummy chat client instead of Ollama
var dummyChatClient = new DummyChatClient();
builder.Services.AddSingleton<IScrumUpdateGenerator>(dummyChatClient);
builder.Services.AddChatClient(dummyChatClient)
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

app.MapGet("/api/jira/issues", async (HttpContext context, AtlassianOAuthService authService, int? maxResults, CancellationToken cancellationToken) =>
{
    try
    {
        var issues = await authService.GetMyOpenIssuesAsync(context, maxResults ?? 25, cancellationToken);
        return Results.Ok(issues);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
