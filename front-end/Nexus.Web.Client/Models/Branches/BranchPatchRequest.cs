using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Nexus.Web.Client.Models.Branches;

/// <summary>
/// Only supplied fields are sent; code is immutable.
/// </summary>
public sealed class BranchPatchRequest
{
    [MinLength(2, ErrorMessage = "Name must be at least 2 characters.")]
    [MaxLength(120, ErrorMessage = "Name must be at most 120 characters.")]
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [MinLength(2, ErrorMessage = "City must be at least 2 characters.")]
    [MaxLength(80, ErrorMessage = "City must be at most 80 characters.")]
    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("isActive")]
    public bool? IsActive { get; set; }
}
