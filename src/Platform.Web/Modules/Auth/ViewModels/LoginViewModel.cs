using System.ComponentModel.DataAnnotations;

namespace Platform.Web.Modules.Auth.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "Email wajib diisi.")]
    [EmailAddress(ErrorMessage = "Format email tidak valid.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password wajib diisi.")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Ingat saya")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}
