namespace Aura.Application.Common.Security;

public static class ProjectFilePermissionConvention
{
    public const string ResourceType = "ProjectFile";
    public const string AccessLevel = "Read";
    public const string GrantedStatus = "Granted";
    public const string RevokedStatus = "Revoked";

    public static string FormatResourceIdentifier(Guid fileId) => fileId.ToString("D");
}
