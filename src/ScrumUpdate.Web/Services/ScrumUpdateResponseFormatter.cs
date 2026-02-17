namespace ScrumUpdate.Web.Services;

public static class ScrumUpdateResponseFormatter
{
    public static string Format(GeneratedScrumUpdate scrumUpdate)
    {
        return $"""
            Scrum update for {scrumUpdate.ScrumDate:yyyy-MM-dd}
            Generated at: {scrumUpdate.GeneratedTimeUtc:O}

            Yesterday: {scrumUpdate.WhatIDidYesterday}
            Today: {scrumUpdate.WhatIPlanToDoToday}
            Blocker: {scrumUpdate.Blocker}
            """;
    }
}
