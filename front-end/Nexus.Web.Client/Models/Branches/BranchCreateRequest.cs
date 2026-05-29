using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Nexus.Web.Client.Models.Branches;

public sealed class BranchCreateRequest
{
    [Required(ErrorMessage = "Branch code is required.")]
    [RegularExpression(@"^[A-Z]{2,5}$", ErrorMessage = "Code must be 2–5 uppercase letters (e.g. BLR).")]
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Branch name is required.")]
    [MinLength(2, ErrorMessage = "Name must be at least 2 characters.")]
    [MaxLength(120, ErrorMessage = "Name must be at most 120 characters.")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "City is required.")]
    [MinLength(2, ErrorMessage = "City must be at least 2 characters.")]
    [MaxLength(80, ErrorMessage = "City must be at most 80 characters.")]
    [JsonPropertyName("city")]
    public string City { get; set; } = string.Empty;

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; } = true;
}
