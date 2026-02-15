using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using ScrumUpdate.Web.Components;
using ScrumUpdate.Web.Data;
using ScrumUpdate.Web.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Add Entity Framework with in-memory provider
builder.Services.AddDbContext<ChatDbContext>(options =>
    options.UseInMemoryDatabase("ChatDatabase"));

builder.Services.AddScoped<ChatSessionService>();

builder.Services.AddHttpClient("WebClient", client => client.Timeout = TimeSpan.FromSeconds(600));

// Use the dummy chat client instead of Ollama
var dummyChatClient = new DummyChatClient();
builder.Services.AddSingleton<IScrumUpdateGenerator>(dummyChatClient);
builder.Services.AddChatClient(dummyChatClient)
    .UseFunctionInvocation()
    //.UseLogging()
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
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
