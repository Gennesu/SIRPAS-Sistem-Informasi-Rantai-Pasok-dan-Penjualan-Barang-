using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Web.Modules.Auth.Contracts;
using Platform.Web.Modules.Consumers.Contracts;
using Platform.Web.Modules.Consumers.ViewModels;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Consumers.Controllers;

/// <summary>Akun pembeli akhir: pendaftaran publik (selalu role CONSUMER) dan profil individu.</summary>
public class ConsumerController : Controller
{
    private readonly IConsumerService _consumers;
    private readonly ISignInService _signIn;

    public ConsumerController(IConsumerService consumers, ISignInService signIn)
    {
        _consumers = consumers;
        _signIn = signIn;
    }

    [HttpGet("register-pembeli"), AllowAnonymous]
    public IActionResult Register()
    {
        return User.Identity?.IsAuthenticated == true ? RedirectToAction("Index", "Home") : View(new RegisterConsumerViewModel());
    }

    [HttpPost("register-pembeli"), AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterConsumerViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _consumers.RegisterAsync(
            new RegisterConsumerRequest(model.FullName, model.Email, model.Password, model.Phone, model.Address, model.Latitude, model.Longitude), ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Pendaftaran gagal.");
            return View(model);
        }

        await _signIn.SignInAsync(result.Value!);
        TempData["Success"] = "Akun pembeli dibuat. Selamat berbelanja!";
        return RedirectToAction("Index", "Home");
    }

    [Authorize(Roles = RoleNames.Consumer)]
    public async Task<IActionResult> Profile(CancellationToken ct)
    {
        var profile = await _consumers.GetProfileAsync(ct);
        if (!profile.Succeeded)
        {
            return NotFound();
        }

        var p = profile.Value!;
        return View(new ConsumerProfileViewModel
        {
            FullName = p.FullName, Email = p.Email, Phone = p.Phone, Address = p.Address, Latitude = p.Latitude, Longitude = p.Longitude
        });
    }

    [HttpPost, Authorize(Roles = RoleNames.Consumer), ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ConsumerProfileViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _consumers.UpdateProfileAsync(
            new UpdateConsumerProfileRequest(model.FullName, model.Phone, model.Address, model.Latitude, model.Longitude), ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Profil gagal disimpan.");
            return View(model);
        }

        TempData["Success"] = "Profil disimpan.";
        return RedirectToAction(nameof(Profile));
    }
}
