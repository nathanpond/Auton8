using AutoNate.Web.Models.Menus;

namespace AutoNate.Web.Services.Menus;

public interface IPageTemplateStore
{
    Task<IReadOnlyList<PageTemplate>> ListEnabledAsync(CancellationToken cancellationToken = default);

    Task<PageTemplate?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);
}
