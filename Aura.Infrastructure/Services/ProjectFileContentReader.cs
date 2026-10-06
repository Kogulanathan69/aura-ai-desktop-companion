using System.Security;
using System.Text;
using Aura.Application.Common.Exceptions;
using Aura.Application.ProjectFiles.Content;
using Aura.Application.ProjectFiles.Validation;

namespace Aura.Infrastructure.Services;

public sealed class ProjectFileContentReader(ProjectFileAccessOptions options) : IProjectFileContentReader
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".sln", ".slnx", ".json", ".xml", ".config", ".md", ".txt",
        ".ts", ".tsx", ".js", ".jsx", ".html", ".css", ".scss", ".sql", ".py", ".dart", ".yaml", ".yml"
    };

    public async Task<ProjectFileReadResult> ReadAsync(Guid localUserId, string? projectRoot,
        string relativePath, string fileName, string extension, CancellationToken cancellationToken = default)
    {
        // Must precede even path resolution or filesystem metadata inspection.
        if (!options.Enabled) return new(ProjectFileContentStatus.Unavailable);
        if (!OperatingSystem.IsWindows()) return new(ProjectFileContentStatus.UnsafeOrUnreadable);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (options.AllowedRootsByUser is null ||
                !options.AllowedRootsByUser.TryGetValue(localUserId.ToString("D"), out var configuredRoots) ||
                configuredRoots is null || configuredRoots.Length == 0)
                return new(ProjectFileContentStatus.UnsafeOrUnreadable);
            var roots = configuredRoots.Select(CanonicalRoot).ToArray();
            var project = CanonicalRoot(projectRoot);
            var allowed = roots.FirstOrDefault(root => Contains(root, project));
            if (allowed is null) return new(ProjectFileContentStatus.UnsafeOrUnreadable);

            var metadata = ProjectFileValidation.Normalize(relativePath, fileName, extension);
            var parts = metadata.RelativePath.Split('/');
            if (parts.Any(part => !SafeComponent(part))) return new(ProjectFileContentStatus.UnsafeOrUnreadable);
            var candidate = Path.GetFullPath(Path.Combine(project, metadata.RelativePath.Replace('/', '\\')));
            if (!Contains(project, candidate) || string.Equals(project, candidate, StringComparison.OrdinalIgnoreCase))
                return new(ProjectFileContentStatus.UnsafeOrUnreadable);
            var actualExtension = Path.GetExtension(candidate);
            if (!TextExtensions.Contains(actualExtension) ||
                !string.Equals(actualExtension, metadata.Extension, StringComparison.OrdinalIgnoreCase))
                return new(ProjectFileContentStatus.UnsupportedType);

            // Check every ancestor from the volume to the final file. These managed checks are NOT race-free.
            CheckChain(allowed, true);
            CheckChain(project, true);
            CheckChain(candidate, false);
            var info = new FileInfo(candidate);
            if (info.Length > ProjectFileAccessOptions.MaximumBytes) return new(ProjectFileContentStatus.TooLarge);
            await using var stream = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > ProjectFileAccessOptions.MaximumBytes) return new(ProjectFileContentStatus.TooLarge);
            var buffer = new byte[ProjectFileAccessOptions.MaximumBytes + 1];
            var total = 0;
            while (total < buffer.Length)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken);
                if (count == 0) break;
                total += count;
            }
            if (total > ProjectFileAccessOptions.MaximumBytes) return new(ProjectFileContentStatus.TooLarge);
            if (buffer.AsSpan(0, total).Contains((byte)0)) return new(ProjectFileContentStatus.UnsafeOrUnreadable);
            var offset = total >= 3 && buffer[0] == 0xef && buffer[1] == 0xbb && buffer[2] == 0xbf ? 3 : 0;
            var content = new UTF8Encoding(false, true).GetString(buffer, offset, total - offset);
            cancellationToken.ThrowIfCancellationRequested();
            return new(ProjectFileContentStatus.Success, content);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or SecurityException or AppValidationException)
        {
            return new(ProjectFileContentStatus.UnsafeOrUnreadable);
        }
    }

    private static string CanonicalRoot(string? value)
    {
        // Windows-first V1 accepts only fully qualified drive paths, never UNC/device/volume roots.
        if (string.IsNullOrWhiteSpace(value) || value.Length < 3 || !char.IsAsciiLetter(value[0]) ||
            value[1] != ':' || (value[2] != '\\' && value[2] != '/') || !Path.IsPathFullyQualified(value))
            throw new ArgumentException("Invalid root.");
        var components = value[3..].Replace('\\', '/').TrimEnd('/').Split('/');
        if (components.Any(component => !SafeComponent(component))) throw new ArgumentException("Invalid root.");
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
        if (string.Equals(canonical, Path.GetPathRoot(canonical), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid root.");
        return canonical;
    }

    private static bool SafeComponent(string value)
    {
        if (value.Length == 0 || value is "." or ".." || value.EndsWith('.') || value.EndsWith(' ') ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        var stem = value.Split('.')[0];
        return !new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem, StringComparer.OrdinalIgnoreCase) &&
            !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                "123456789¹²³".Contains(stem[3]));
    }

    private static bool Contains(string root, string candidate) =>
        string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase) ||
        candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void CheckChain(string path, bool directory)
    {
        var volume = Path.GetPathRoot(path) ?? throw new IOException();
        var current = volume;
        CheckAttributes(current, true);
        var parts = path[volume.Length..].Split(Path.DirectorySeparatorChar);
        for (var index = 0; index < parts.Length; index++)
        {
            current = Path.Combine(current, parts[index]);
            CheckAttributes(current, index < parts.Length - 1 || directory);
        }
    }

    private static void CheckAttributes(string path, bool directory)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0 ||
            ((attributes & FileAttributes.Directory) != 0) != directory)
            throw new IOException();
    }
}
