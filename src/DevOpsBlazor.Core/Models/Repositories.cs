namespace DevOpsBlazor.Core.Models;

public sealed record Repository(
    Guid Id,
    string Name,
    Guid ProjectId,
    string Project,
    string? DefaultBranch,
    bool IsDisabled,
    long Size)
{
    public string FullName => $"{Project}/{Name}";
}
