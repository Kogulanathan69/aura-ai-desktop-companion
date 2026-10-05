namespace Aura.Application.ProjectSessions.Validation;

public static class ProjectSessionValidation
{
    // CurrentTask and Summary are PostgreSQL text columns with no length limit.
    public static string? NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
