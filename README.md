# Scrum Update

Scrum Update is a Blazor-based chat application built with .NET 10 that provides a conversational interface. This application demonstrates a simple chat interface with a dummy AI response for testing and development purposes.

>[!NOTE]
> This application uses a dummy AI client that returns simulated responses. No external AI service or Ollama installation is required to run this application.

## Features

- **Blazor Server** - Interactive web UI with real-time updates
- **Dummy AI Client** - Simulated responses for testing without external dependencies
- **.NET 10** - Built with the latest .NET framework
- **Easy to extend** - Replace the dummy client with your preferred AI provider (Ollama, Azure OpenAI, etc.)

# Setup

## Prerequisites

- **.NET 10 SDK** - Required to build and run the application
- **Visual Studio 2022+** or **Visual Studio Code** with C# Dev Kit


# Running the application

## Using Visual Studio

1. Open the `.sln` file in Visual Studio.
2. Press `Ctrl+F5` or click the "Start" button in the toolbar to run the project.

## Using Visual Studio Code

1. Open the project folder in Visual Studio Code.
2. Install the [C# Dev Kit extension](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit) for Visual Studio Code.
3. Once installed, Open the `Program.cs` file in the ScrumUpdate.AppHost project.
4. Run the project by clicking the "Run" button in the Debug view.

## Trust the localhost certificate

Several Aspire templates include ASP.NET Core projects that are configured to use HTTPS by default. If this is the first time you're running the project, an exception might occur when loading the Aspire dashboard. This error can be resolved by trusting the self-signed development certificate with the .NET CLI.

See [Troubleshoot untrusted localhost certificate in Aspire](https://learn.microsoft.com/dotnet/aspire/troubleshooting/untrusted-localhost-certificate) for more information.

# Updating JavaScript dependencies

This template leverages JavaScript libraries to provide essential functionality. These libraries are located in the wwwroot/lib folder of the ScrumUpdate.Web project. For instructions on updating each dependency, please refer to the README.md file in each respective folder.

## Customizing the AI Client

The application currently uses a `DummyChatClient` that returns simulated responses. To integrate a real AI provider:

1. Modify `Program.cs` in the ScrumUpdate.Web project
2. Replace the `DummyChatClient` registration with your preferred AI provider
3. Example with Ollama:
   ```csharp
   // Instead of:
   // IChatClient chatClient = new DummyChatClient();

   // Use:
   IChatClient chatClient = new OllamaApiClient(new Uri("http://localhost:11434"), "llama2");
   ```

# Learn More
To learn more about development with .NET and AI, check out the following links:

* [AI for .NET Developers](https://learn.microsoft.com/dotnet/ai/)
