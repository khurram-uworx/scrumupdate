using System.ComponentModel;

namespace ScrumUpdate.Web.Services;

public sealed class ScrumUpdateTools(JiraScrumUpdateDraftService jiraScrumUpdateDraftService)
{
    readonly Queue<GeneratedScrumUpdate> generatedUpdates = [];

    [Description("Generate the user's scrum update draft from Jira activity. Use this when the user asks for a scrum update or asks to regenerate one.")]
    public async Task<string> GenerateScrumUpdateDraftAsync(CancellationToken cancellationToken = default)
    {
        var draft = await jiraScrumUpdateDraftService.TryGenerateAsync(cancellationToken);
        if (draft is null)
        {
            return "Unable to generate scrum update from Jira. Ask the user to connect Jira first.";
        }

        lock (generatedUpdates)
        {
            generatedUpdates.Enqueue(draft);
        }

        return ScrumUpdateResponseFormatter.Format(draft);
    }

    public void ResetCapturedGeneratedUpdates()
    {
        lock (generatedUpdates)
        {
            generatedUpdates.Clear();
        }
    }

    public GeneratedScrumUpdate? TryConsumeLatestGeneratedUpdate()
    {
        lock (generatedUpdates)
        {
            if (generatedUpdates.Count == 0)
            {
                return null;
            }

            GeneratedScrumUpdate? latest = null;
            while (generatedUpdates.Count > 0)
            {
                latest = generatedUpdates.Dequeue();
            }

            return latest;
        }
    }
}
