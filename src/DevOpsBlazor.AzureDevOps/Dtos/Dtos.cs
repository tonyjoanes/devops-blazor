using System.Text.Json;

namespace DevOpsBlazor.AzureDevOps.Dtos;

// Shapes of the Azure DevOps REST responses (api-version 7.1). Only the fields the app uses are declared.

public sealed record ListResponse<T>(int Count, List<T>? Value);

public sealed record NamedRef(string? Id, string? Name);

public sealed record IdentityRef(string? Id, string? DisplayName, string? UniqueName);

public sealed record ConnectionDataDto(IdentityRef? AuthenticatedUser, ConnectionUserDto? AuthorizedUser);

public sealed record ConnectionUserDto(string? Id, string? ProviderDisplayName);

public sealed record ProjectDto(Guid Id, string Name, string? State);

public sealed record DefinitionDto(
    int Id,
    string Name,
    string? Path,
    string? QueueStatus,
    ProcessDto? Process,
    DefinitionRepositoryDto? Repository,
    NamedRef? Project);

public sealed record ProcessDto(int Type);

public sealed record DefinitionRepositoryDto(string? Id, string? Name, string? Type, string? DefaultBranch);

public sealed record BuildDto(
    int Id,
    string? BuildNumber,
    string? Status,
    string? Result,
    DateTimeOffset? QueueTime,
    DateTimeOffset? StartTime,
    DateTimeOffset? FinishTime,
    string? SourceBranch,
    string? SourceVersion,
    string? Reason,
    IdentityRef? RequestedFor,
    BuildDefinitionRefDto? Definition,
    NamedRef? Project,
    BuildRepositoryDto? Repository);

public sealed record BuildDefinitionRefDto(int Id, string? Name);

public sealed record BuildRepositoryDto(string? Id, string? Name, string? Type);

public sealed record TimelineDto(List<TimelineRecordDto>? Records);

public sealed record TimelineRecordDto(
    Guid Id,
    Guid? ParentId,
    string? Type,
    string? Name,
    string? Result,
    string? State,
    DateTimeOffset? StartTime,
    DateTimeOffset? FinishTime,
    int? ErrorCount,
    int? WarningCount,
    int? Attempt,
    int? Order,
    string? WorkerName,
    LogRefDto? Log,
    List<IssueDto>? Issues);

public sealed record LogRefDto(int Id);

public sealed record IssueDto(string? Type, string? Message);

public sealed record ChangeDto(string? Id, string? Message, IdentityRef? Author, DateTimeOffset? Timestamp, string? DisplayUri);

public sealed record RepositoryDto(
    Guid Id,
    string Name,
    string? DefaultBranch,
    bool? IsDisabled,
    long? Size,
    ProjectDto? Project);

public sealed record PolicyConfigurationDto(
    int Id,
    bool IsEnabled,
    bool IsBlocking,
    bool? IsDeleted,
    PolicyTypeRefDto? Type,
    JsonElement Settings);

public sealed record PolicyTypeRefDto(Guid Id, string? DisplayName);
