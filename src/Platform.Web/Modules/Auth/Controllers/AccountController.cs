using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Platform.Web.Modules.Auth.Contracts;
using Platform.Web.Modules.Auth.ViewModels;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Auth.Controllers;

public class AccountController : Controller
{
    private readonly IAuthService _auth;
    private readonly ISignInService _signIn;
    private readonly DemoOptions _demo;
    private readonly IReadOnlyList<ILoginGate> _gates;

    public AccountController(IAuthService auth, ISignInService signIn, IOptions<DemoOptions> demo, IEnumerable<ILoginGate> gates)
    {
        _auth = auth;
        _signIn = signIn;
        _demo = demo.Value;
        _gates = gates.ToList();
    }

    [HttpGet, AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        ShowDemoAccounts();
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost, AllowAnonymous]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken ct)
    {
        ShowDemoAccounts();
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _auth.ValidateCredentialsAsync(model.Email, model.Password, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Login gagal. Silakan coba lagi.");
            return View(model);
        }

        // Modul lain boleh menolak login (mis. usaha dinonaktifkan admin).
        foreach (var gate in _gates)
        {
            var denial = await gate.CheckAsync(result.Value!, ct);
            if (denial is not null)
            {
                ModelState.AddModelError(string.Empty, denial);
                return View(model);
            }
        }

        await _signIn.SignInAsync(result.Value!, model.RememberMe);

        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
        {
            return LocalRedirect(model.ReturnUrl);
        }

        return RedirectToAction("Index", "Home");
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> Logout()
    {
        await _signIn.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    // Mode demo: tampilkan daftar akun dummy di halaman login.
    private void ShowDemoAccounts()
    {
        if (_demo.SeedDummyData)
        {
            ViewData["DemoAccounts"] = DemoAccounts.All;
        }
    }
}
