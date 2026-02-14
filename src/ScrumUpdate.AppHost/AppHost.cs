var builder = DistributedApplication.CreateBuilder(args);

var webApp = builder.AddProject<Projects.ScrumUpdate_Web>("aichatweb-app");

builder.Build().Run();
