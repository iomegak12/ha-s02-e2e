using System.ComponentModel.DataAnnotations;

namespace Nexus.Patients.Api.Configuration;

/// <summary>Bound from <c>Purge:*</c>.</summary>
public sealed class PurgeOptions
{
    public const string SectionName = "Purge";

    /// <summary>If <c>false</c> the worker does not run.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Days an Archived patient is retained before hard-delete.</summary>
    [Range(1, 3650)] public int RetentionDays { get; set; } = 30;

    /// <summary>How often the worker wakes up to scan for due rows.</summary>
    [Range(1, 1440)] public int IntervalMinutes { get; set; } = 60;

    /// <summary>Maximum number of patients purged per cycle.</summary>
    [Range(1, 1000)] public int BatchSize { get; set; } = 50;

    /// <summary>Placeholder used by the audit redactor to overwrite <c>Summary</c>.</summary>
    public string RedactionPlaceholder { get; set; } = "[REDACTED]";
}
