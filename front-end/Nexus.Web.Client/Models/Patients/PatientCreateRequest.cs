using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Nexus.Web.Client.Models.Patients;

/// <summary>Maps to the PatientCreate schema in patients.openapi.json.</summary>
public sealed class PatientCreateRequest
{
    [Required(ErrorMessage = "First name is required.")]
    [StringLength(100, MinimumLength = 1)]
    [JsonPropertyName("firstName")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Last name is required.")]
    [StringLength(100, MinimumLength = 1)]
    [JsonPropertyName("lastName")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Date of birth is required.")]
    [JsonPropertyName("dateOfBirth")]
    public string DateOfBirth { get; set; } = string.Empty;   // "yyyy-MM-dd"

    [Required(ErrorMessage = "Gender is required.")]
    [JsonPropertyName("gender")]
    public string Gender { get; set; } = string.Empty;

    [Required(ErrorMessage = "Primary phone is required.")]
    [StringLength(32)]
    [JsonPropertyName("primaryPhone")]
    public string PrimaryPhone { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(256)]
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [Required(ErrorMessage = "Address line 1 is required.")]
    [StringLength(200)]
    [JsonPropertyName("addressLine1")]
    public string AddressLine1 { get; set; } = string.Empty;

    [StringLength(200)]
    [JsonPropertyName("addressLine2")]
    public string? AddressLine2 { get; set; }

    [Required(ErrorMessage = "City is required.")]
    [StringLength(100)]
    [JsonPropertyName("city")]
    public string City { get; set; } = string.Empty;

    [Required(ErrorMessage = "State / Province is required.")]
    [StringLength(100)]
    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [Required(ErrorMessage = "Postal code is required.")]
    [StringLength(20)]
    [JsonPropertyName("postalCode")]
    public string PostalCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Country is required.")]
    [StringLength(2, MinimumLength = 2, ErrorMessage = "Country must be a 2-letter ISO code.")]
    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;

    [Required(ErrorMessage = "Primary branch is required.")]
    [JsonPropertyName("primaryBranchId")]
    public string PrimaryBranchId { get; set; } = string.Empty;
}
