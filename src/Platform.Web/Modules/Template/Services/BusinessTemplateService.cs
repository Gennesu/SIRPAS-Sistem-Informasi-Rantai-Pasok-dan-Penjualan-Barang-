using Platform.Web.Modules.BusinessProfile.Contracts;
using Platform.Web.Modules.Template.Contracts;
using Platform.Web.Modules.Template.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Template.Services;

public sealed class BusinessTemplateService : IBusinessTemplateService
{
    private readonly IBusinessContext _context;
    private readonly IBusinessService _business;

    public BusinessTemplateService(IBusinessContext context, IBusinessService business)
    {
        _context = context;
        _business = business;
    }

    public async Task<BusinessTemplate> GetCurrentAsync(CancellationToken ct = default)
    {
        var businessId = _context.BusinessId;
        if (businessId is null)
        {
            return TemplateCatalog.Other;
        }

        var profile = await _business.GetProfileAsync(businessId.Value, ct);
        return TemplateCatalog.For(profile.Value?.BusinessTypeCode);
    }
}
