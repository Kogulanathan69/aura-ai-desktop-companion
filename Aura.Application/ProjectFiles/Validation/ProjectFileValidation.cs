using Aura.Application.Common.Exceptions;

namespace Aura.Application.ProjectFiles.Validation;

public static class ProjectFileValidation
{
    public static (string RelativePath, string FileName, string Extension) Normalize(
        string? relativePath, string? fileName, string? extension)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(fileName))
            throw new AppValidationException("Relative path and file name are required.");

        var path = relativePath.Trim().Replace('\\', '/');
        var name = fileName.Trim();
        var ext = extension?.Trim() ?? string.Empty;
        var parts = path.Split('/');

        if (path.StartsWith('/') || path.Contains(':') ||
            parts.Any(part => part.Length == 0 || part is "." or "..") ||
            name is "." or ".." || name.Contains('/') || name.Contains('\\') || name.Contains(':') ||
            path.Contains('\0') || name.Contains('\0') || ext.Contains('\0'))
            throw new AppValidationException("File metadata must use a valid relative path and file name.");

        if (!string.Equals(parts[^1], name, StringComparison.Ordinal))
            throw new AppValidationException("File name must match the final relative path component.");

        if (name.Length > 255 || ext.Length > 30)
            throw new AppValidationException("File name or extension exceeds its allowed length.");

        if (ext.Contains('/') || ext.Contains('\\') || ext.Contains(':') ||
            (ext.Length > 0 && (!ext.StartsWith('.') || !name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))))
            throw new AppValidationException("Extension must match the file name or be empty.");

        return (path, name, ext);
    }
}
