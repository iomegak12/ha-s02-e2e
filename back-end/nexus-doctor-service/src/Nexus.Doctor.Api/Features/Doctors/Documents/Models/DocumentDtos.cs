using System.Text.Json.Serialization;
using Nexus.Doctors.Api.Features.Doctors.Models;

namespace Nexus.Doctors.Api.Features.Doctors.Documents.Models;

/// <summary>Wire shape for a doctor document (matches <c>specs/doctors.openapi.json</c> <c>Document</c>).</summary>
public sealed class DocumentDto
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    [JsonPropertyName("fileName")] public string FileName { get; init; } = string.Empty;
    [JsonPropertyName("contentType")] public string ContentType { get; init; } = string.Empty;
    [JsonPropertyName("sizeBytes")] public long SizeBytes { get; init; }
    [JsonPropertyName("status")] public string Status { get; init; } = "Uploaded";
    [JsonPropertyName("uploadedAtUtc")] public DateTime UploadedAtUtc { get; init; }
    [JsonPropertyName("reviewedAtUtc")] public DateTime? ReviewedAtUtc { get; init; }
    [JsonPropertyName("reviewerId")] public Guid? ReviewerId { get; init; }
    [JsonPropertyName("rejectionReason")] public string? RejectionReason { get; init; }
    [JsonPropertyName("storagePath")] public string StoragePath { get; init; } = string.Empty;

    public static DocumentDto From(DoctorDocument d) => new()
    {
        Id = d.Id,
        Kind = d.Kind,
        FileName = d.FileName,
        ContentType = d.ContentType,
        SizeBytes = d.SizeBytes,
        Status = d.Status.ToString(),
        UploadedAtUtc = d.UploadedAtUtc,
        ReviewedAtUtc = d.ReviewedAtUtc,
        RejectionReason = d.Status == DocumentStatus.Rejected ? d.ReviewNote : null,
        StoragePath = d.StorageKey,
    };
}

/// <summary>PATCH body for <c>reviewDoctorDocument</c>.</summary>
public sealed class DocumentReviewRequest
{
    public string Status { get; init; } = string.Empty;
    public string? RejectionReason { get; init; }
}
