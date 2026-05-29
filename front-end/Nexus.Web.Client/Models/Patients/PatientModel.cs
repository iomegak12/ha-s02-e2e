using System.Text.Json.Serialization;

namespace Nexus.Web.Client.Models.Patients;

public sealed class PatientModel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("firstName")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("lastName")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("dateOfBirth")]
    public string DateOfBirth { get; set; } = string.Empty;   // ISO-8601 date "yyyy-MM-dd"

    [JsonPropertyName("gender")]
    public string Gender { get; set; } = string.Empty;         // Male | Female | Other | Unknown

    [JsonPropertyName("primaryPhone")]
    public string PrimaryPhone { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("addressLine1")]
    public string AddressLine1 { get; set; } = string.Empty;

    [JsonPropertyName("addressLine2")]
    public string? AddressLine2 { get; set; }

    [JsonPropertyName("city")]
    public string City { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("postalCode")]
    public string PostalCode { get; set; } = string.Empty;

    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;       // ISO 3166-1 alpha-2

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;         // Draft | Active | Archived

    [JsonPropertyName("primaryBranchId")]
    public string PrimaryBranchId { get; set; } = string.Empty;

    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; set; }

    [JsonPropertyName("updatedAtUtc")]
    public DateTimeOffset UpdatedAtUtc { get; set; }

    // ── helpers ───────────────────────────────────────────────────────────────
    public string FullName => $"{FirstName} {LastName}".Trim();

    public string Initials =>
        $"{(FirstName.Length > 0 ? FirstName[0] : ' ')}{(LastName.Length > 0 ? LastName[0] : ' ')}".Trim().ToUpperInvariant();

    public string FormattedDob =>
        DateTimeOffset.TryParse(DateOfBirth, out var dt)
            ? dt.ToString("dd MMM yyyy")
            : DateOfBirth;
}
