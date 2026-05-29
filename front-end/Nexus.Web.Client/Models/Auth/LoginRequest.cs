using System.ComponentModel.DataAnnotations;

namespace Nexus.Web.Client.Models.Auth;

public sealed class LoginRequest
{
    [Required(ErrorMessage = "Username is required")]
    public string Username { get; set; } = "";

    [Required(ErrorMessage = "Password is required")]
    public string Password { get; set; } = "";
}
