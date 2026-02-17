using System.ComponentModel;

namespace ScrumUpdate.Web.Services;

public sealed class ScrumUpdateTools(JiraScrumUpdateDraftService jiraScrumUpdateDraftService)
{
    [Description("Generate the user's scrum update draft from Jira activity. Use this when the user asks for a scrum update or asks to regenerate one.")]
    public async Task<string> GenerateScrumUpdateDraftAsync(CancellationToken cancellationToken = default)
    {
        var draft = await jiraScrumUpdateDraftService.TryGenerateAsync(cancellationToken);
        if (draft is null)
        {
            return "Unable to generate scrum update from Jira. Ask the user to connect Jira first.";
        }

        return ScrumUpdateResponseFormatter.Format(draft);
    }
}
