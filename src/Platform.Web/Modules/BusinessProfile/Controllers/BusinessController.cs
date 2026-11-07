using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Platform.Web.Modules.Auth.Contracts;
using Platform.Web.Modules.BusinessProfile.Contracts;
using Platform.Web.Modules.BusinessProfile.ViewModels;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.BusinessProfile.Controllers;

public class BusinessController : Controller
{
    private readonly IBusinessService _business;
    private readonly ISignInService _signIn;
    private readonly IBusinessContext _context;

    public BusinessController(IBusinessService business, ISignInService signIn, IBusinessContext context)
    {
        _business = business;
        _signIn = signIn;
        _context = context;
    }

    // ------------------------------------------------------------ Registrasi usaha

    [HttpGet("register"), AllowAnonymous]
    public async Task<IActionResult> Register(CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        return View(await WithBusinessTypesAsync(new RegisterViewModel(), ct));
    }

    [HttpPost("register"), AllowAnonymous]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(await WithBusinessTypesAsync(model, ct));
        }

        var request = new RegisterBusinessRequest(
            model.OwnerName,
            model.Email,
            model.Password,
            model.BusinessName,
            model.BusinessTypeId,
            model.MarketRole,
            model.Address,
            model.Latitude,
            model.Longitude,
            model.Phone);

        var result = await _business.RegisterAsync(request, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Pendaftaran gagal. Silakan coba lagi.");
            return View(await WithBusinessTypesAsync(model, ct));
        }

        await _signIn.SignInAsync(result.Value!);
        TempData["Success"] = "Pendaftaran berhasil. Usaha Anda menunggu verifikasi admin.";
        return RedirectToAction("Index", "Home");
    }

    // ------------------------------------------------------------ Profil usaha

    [HttpGet("business/profile/{id:int?}"), Authorize]
    public async Task<IActionResult> Profile(int? id, CancellationToken ct)
    {
        // Tanpa id = usaha milik sendiri. BusinessId milik sendiri selalu dari claim login.
        var targetId = id ?? _context.BusinessId;
        if (targetId is null)
        {
            return NotFound();
        }

        var result = await _business.GetProfileAsync(targetId.Value, ct);
        if (!result.Succeeded)
        {
            return NotFound();
        }

        return View(BusinessProfileViewModel.From(result.Value!, canEdit: _context.BusinessId == targetId));
    }

    [HttpPost("business/profile/{id:int}"), Authorize]
    public async Task<IActionResult> Profile(
        int id,
        [Bind(Prefix = nameof(BusinessProfileViewModel.Form))] EditBusinessInput form,
        CancellationToken ct)
    {
        var current = await _business.GetProfileAsync(id, ct);
        if (!current.Succeeded)
        {
            return NotFound();
        }

        var view = BusinessProfileViewModel.From(current.Value!, canEdit: _context.BusinessId == id);
        view.Form = form; // pertahankan isian pengguna bila perlu ditampilkan ulang

        if (!ModelState.IsValid)
        {
            return View(view);
        }

        var update = await _business.UpdateProfileAsync(
            id,
            new UpdateBusinessRequest(form.Name, form.Address, form.Latitude, form.Longitude, form.Phone, form.ShareSalesData),
            ct);

        if (!update.Succeeded)
        {
            if (update.Kind is ErrorKind.NotFound or ErrorKind.Forbidden)
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty, update.Error ?? "Profil gagal disimpan.");
            return View(view);
        }

        TempData["Success"] = "Profil usaha diperbarui.";
        return RedirectToAction(nameof(Profile), new { id });
    }

    private async Task<RegisterViewModel> WithBusinessTypesAsync(RegisterViewModel model, CancellationToken ct)
    {
        var types = await _business.GetBusinessTypesAsync(ct);
        model.BusinessTypes = types
            .Select(t => new SelectListItem(t.Name, t.Id.ToString(), t.Id == model.BusinessTypeId))
            .ToList();
        return model;
    }
}
