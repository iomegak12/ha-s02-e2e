using System.ComponentModel.DataAnnotations;

namespace Nexus.Doctors.Api.Configuration;

/// <summary>Bound from <c>Documents:*</c>.</summary>
public sealed class DocumentsOptions
{
    public const string SectionName = "Documents";

    /// <summary>Local-filesystem root for document storage.</summary>
    [Required] public string RootPath { get; set; } = "/var/data/nexus/doctor-docs/";

    /// <summary>Maximum file size — 10 MB by default.</summary>
    [Range(1, 1024L * 1024L * 1024L)] public long MaxFileBytes { get; set; } = 10L * 1024L * 1024L;

    /// <summary>Accepted MIME types.</summary>
    public string[] AllowedContentTypes { get; set; } =
    {
        "application/pdf", "image/png", "image/jpeg",
    };
}
