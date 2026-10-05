using Aura.Application.Common.Exceptions;

namespace Aura.Application.Projects.Validation;

public static class ProjectValidation
{
    public const int NameMaxLength = 150;
    public const int CurrentBranchMaxLength = 150;

    private static readonly string[] AllowedStatuses =
        ["Active", "Paused", "Completed", "Archived"];

    public static void ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new AppValidationException("Project name is required.");
        }

        if (name.Trim().Length > NameMaxLength)
        {
            throw new AppValidationException(
                $"Project name cannot exceed {NameMaxLength} characters.");
        }
    }

    public static void ValidateStatus(string? status)
    {
        _ = NormalizeStatus(status);
    }

    public static string NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            throw new AppValidationException("Project status is required.");
        }

        var normalized = AllowedStatuses.FirstOrDefault(
            value => string.Equals(value, status.Trim(), StringComparison.OrdinalIgnoreCase));

        if (normalized is null)
        {
            throw new AppValidationException(
                "Project status must be Active, Paused, Completed, or Archived.");
        }

        return normalized;
    }

    public static void ValidateCurrentBranch(string? currentBranch)
    {
        _ = NormalizeCurrentBranch(currentBranch);
    }

    public static string? NormalizeCurrentBranch(string? currentBranch)
    {
        if (string.IsNullOrWhiteSpace(currentBranch))
        {
            return null;
        }

        var normalized = currentBranch.Trim();
        if (normalized.Length > CurrentBranchMaxLength)
        {
            throw new AppValidationException(
                $"Current branch cannot exceed {CurrentBranchMaxLength} characters.");
        }

        return normalized;
    }
}
