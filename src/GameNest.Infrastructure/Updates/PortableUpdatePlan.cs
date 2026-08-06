using System.Text.Json.Serialization;

namespace GameNest.Infrastructure.Updates;

public sealed record PortableUpdatePlan(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("currentProcessId")] int CurrentProcessId,
    [property: JsonPropertyName("targetRoot")] string TargetRoot,
    [property: JsonPropertyName("candidateRoot")] string CandidateRoot,
    [property: JsonPropertyName("rollbackRoot")] string RollbackRoot,
    [property: JsonPropertyName("stagingRoot")] string StagingRoot,
    [property: JsonPropertyName("healthFile")] string HealthFile,
    [property: JsonPropertyName("failureFile")] string FailureFile,
    [property: JsonPropertyName("databaseFile")] string DatabaseFile,
    [property: JsonPropertyName("databaseBackupFile")] string DatabaseBackupFile,
    [property: JsonPropertyName("expectedVersion")] string ExpectedVersion);
