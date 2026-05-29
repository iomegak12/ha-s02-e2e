using System.Text.Json.Serialization;

namespace Nexus.Web.Client.Models.Patients;

/// <summary>Maps to the PatientPatch schema — all fields nullable (omitted when null).</summary>
public sealed class PatientPatchRequest
{
    [JsonPropertyName("firstName")]
    public string? FirstName { get; set; }

    [JsonPropertyName("lastName")]
    public string? LastName { get; set; }

    [JsonPropertyName("primaryPhone")]
    public string? PrimaryPhone { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("addressLine1")]
    public string? AddressLine1 { get; set; }

    [JsonPropertyName("addressLine2")]
    public string? AddressLine2 { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("postalCode")]
    public string? PostalCode { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("primaryBranchId")]
    public string? PrimaryBranchId { get; set; }
}
