using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Web.Modules.Notifications.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Notifications.Controllers;

[Authorize]
public class NotificationsController : Controller
{
    private readonly INotificationService _notifications;

    public NotificationsController(INotificationService notifications) => _notifications = notifications;

    public async Task<IActionResult> Index(CancellationToken ct) => View(await _notifications.GetAsync(ct));

    /// <summary>Dipanggil browser tiap 45 detik untuk memperbarui lonceng dan lencana menu (satu GET ringan).</summary>
    [HttpGet]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        var summary = await _notifications.GetAsync(ct);
        return Json(new
        {
            total = summary.Total,
            badges = summary.Items.GroupBy(i => i.Key).ToDictionary(g => g.Key, g => g.Sum(i => i.Count)),
            items = summary.Items.Select(i => new
            {
                key = i.Key,
                tone = i.Tone.ToString().ToLowerInvariant(),
                iconSvg = Render(UiIcons.Svg(i.Icon, 19)),
                title = i.Title,
                detail = i.Detail,
                url = i.Url
            })
        });
    }

    private static string Render(Microsoft.AspNetCore.Html.IHtmlContent content)
    {
        using var writer = new StringWriter();
        content.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }
}
