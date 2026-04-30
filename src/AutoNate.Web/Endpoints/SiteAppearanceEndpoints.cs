using System.Security.Claims;
using System.Text.RegularExpressions;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.SiteSettings;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Endpoints;

public static partial class SiteAppearanceEndpoints
{
    private static readonly Guid AppearanceSettingsId = Guid.Parse("00000000-0000-0000-0001-000000000005");
    private static readonly Guid SeedActorId = Guid.Parse("00000000-0000-0000-0000-000000000000");

    public static IEndpointRouteBuilder MapSiteAppearanceEndpoints(this IEndpointRouteBuilder app)
    {
        var publicGroup = app.MapGroup("/api/appearance").AllowAnonymous();
        publicGroup.MapGet("/", async (AutoNateDbContext db, CancellationToken ct) =>
            Results.Ok(await GetDtoAsync(db, ct)));

        var adminGroup = app.MapGroup("/api/admin/appearance").RequireAuthorization();

        adminGroup.MapGet("/", async (
            AutoNateDbContext db, IAuditEventPublisher auditPublisher, CancellationToken ct) =>
            {
                var dto = await GetDtoAsync(db, ct);
                await auditPublisher.PublishAsync(
                    SiteEventTopic.TopicName,
                    SiteEventTypes.AppearanceViewed,
                    SiteResourceKinds.Appearance,
                    resource: new { siteName = dto.SiteName, logoMode = dto.LogoMode },
                    details: null,
                    ct);
                return Results.Ok(dto);
            })
            .RequireKindPermission(EntityKinds.SiteConfig, Actions.View);

        adminGroup.MapPatch("/", async (
            UpdateSiteAppearanceRequest request,
            HttpContext http,
            AutoNateDbContext db,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var validationError = Validate(request);
            if (validationError is not null)
            {
                return Results.BadRequest(new { error = validationError });
            }

            var entity = await db.SiteAppearanceSettings.FirstOrDefaultAsync(
                x => x.Id == AppearanceSettingsId,
                ct);

            if (entity is null)
            {
                entity = CreateDefaultEntity();
                db.SiteAppearanceSettings.Add(entity);
            }

            Apply(entity, request, GetActorId(http));
            await db.SaveChangesAsync(ct);
            await auditPublisher.PublishAsync(
                SiteEventTopic.TopicName,
                SiteEventTypes.AppearanceUpdated,
                SiteResourceKinds.Appearance,
                resource: new { siteName = entity.SiteName, logoMode = entity.LogoMode },
                details: null,
                ct);
            return Results.Ok(ToDto(entity));
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.SiteConfig, Actions.Edit);

        return app;
    }

    private static async Task<SiteAppearanceDto> GetDtoAsync(AutoNateDbContext db, CancellationToken ct)
    {
        var entity = await db.SiteAppearanceSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == AppearanceSettingsId, ct);

        return entity is null ? ToDto(CreateDefaultEntity()) : ToDto(entity);
    }

    private static void Apply(
        SiteAppearanceSettings entity,
        UpdateSiteAppearanceRequest request,
        Guid actorId)
    {
        entity.SiteName = request.SiteName.Trim();
        entity.LogoMode = request.LogoMode.Trim();
        entity.LogoImageUrl = NormalizeOptional(request.LogoImageUrl);
        entity.LogoIcon = NormalizeOptional(request.LogoIcon);
        entity.LogoText = request.LogoText.Trim();
        entity.LoginTagline = NormalizeOptional(request.LoginTagline);
        entity.LoginCoverImageUrl = NormalizeOptional(request.LoginCoverImageUrl);
        entity.PrimaryAccentColor = NormalizeColor(request.PrimaryAccentColor);
        entity.HeaderBg = NormalizeColor(request.HeaderBg);
        entity.HeaderColor = NormalizeColor(request.HeaderColor);
        entity.TopMenuBg = NormalizeColor(request.TopMenuBg);
        entity.TopMenuLinkColor = NormalizeColor(request.TopMenuLinkColor);
        entity.TopMenuLinkHoverBg = NormalizeColor(request.TopMenuLinkHoverBg);
        entity.TopMenuLinkHoverColor = NormalizeColor(request.TopMenuLinkHoverColor);
        entity.TopMenuLinkActiveBg = NormalizeColor(request.TopMenuLinkActiveBg);
        entity.TopMenuLinkActiveColor = NormalizeColor(request.TopMenuLinkActiveColor);
        entity.SidebarBg = NormalizeColor(request.SidebarBg);
        entity.SidebarLinkColor = NormalizeColor(request.SidebarLinkColor);
        entity.SidebarLinkHoverColor = NormalizeColor(request.SidebarLinkHoverColor);
        entity.SidebarActiveBg = NormalizeColor(request.SidebarActiveBg);
        entity.SidebarActiveColor = NormalizeColor(request.SidebarActiveColor);
        entity.SidebarIconColor = NormalizeColor(request.SidebarIconColor);
        entity.SidebarSubmenuBg = NormalizeColor(request.SidebarSubmenuBg);
        entity.SidebarSectionColor = NormalizeColor(request.SidebarSectionColor);
        entity.SurfaceBg = NormalizeColor(request.SurfaceBg);
        entity.SurfaceSecondaryBg = NormalizeColor(request.SurfaceSecondaryBg);
        entity.SurfaceTextColor = NormalizeColor(request.SurfaceTextColor);
        entity.BorderColor = NormalizeColor(request.BorderColor);
        entity.DropdownBg = NormalizeColor(request.DropdownBg);
        entity.ModalBg = NormalizeColor(request.ModalBg);
        entity.SecondaryButtonBg = NormalizeColor(request.SecondaryButtonBg);
        entity.SecondaryButtonTextColor = NormalizeColor(request.SecondaryButtonTextColor);
        entity.SecondaryButtonBorderColor = NormalizeColor(request.SecondaryButtonBorderColor);
        entity.SecondaryButtonHoverBg = NormalizeColor(request.SecondaryButtonHoverBg);
        entity.SecondaryButtonHoverTextColor = NormalizeColor(request.SecondaryButtonHoverTextColor);
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedBy = actorId;
    }

    private static SiteAppearanceSettings CreateDefaultEntity() =>
        new()
        {
            Id = AppearanceSettingsId,
            SiteName = "Auto Nate",
            LogoMode = "icon",
            LogoImageUrl = null,
            LogoIcon = "fa fa-robot",
            LogoText = "Auto Nate",
            LoginTagline = "Sign in to continue to the automation dashboard",
            LoginCoverImageUrl = "/spa/assets/img/login-bg/login-bg-17.jpg",
            PrimaryAccentColor = "#00acac",
            HeaderBg = "#ffffff",
            HeaderColor = "#212529",
            TopMenuBg = "#20252a",
            TopMenuLinkColor = "#a6aaac",
            TopMenuLinkHoverBg = "#20252a",
            TopMenuLinkHoverColor = "#ffffff",
            TopMenuLinkActiveBg = "#20252a",
            TopMenuLinkActiveColor = "#ffffff",
            SidebarBg = "#ffffff",
            SidebarLinkColor = "#6c757d",
            SidebarLinkHoverColor = "#212529",
            SidebarActiveBg = "#f1f3f5",
            SidebarActiveColor = "#212529",
            SidebarIconColor = "#212529",
            SidebarSubmenuBg = "#ffffff",
            SidebarSectionColor = "#adb5bd",
            SurfaceBg = "#ffffff",
            SurfaceSecondaryBg = "#dee2e6",
            SurfaceTextColor = "#212529",
            BorderColor = "#ced4da",
            DropdownBg = "#ffffff",
            ModalBg = "#ffffff",
            SecondaryButtonBg = "#ffffff",
            SecondaryButtonTextColor = "#495057",
            SecondaryButtonBorderColor = "#6c757d",
            SecondaryButtonHoverBg = "#f1f3f5",
            SecondaryButtonHoverTextColor = "#212529",
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = SeedActorId,
            UpdatedAtUtc = DateTime.UtcNow,
            UpdatedBy = SeedActorId
        };

    private static SiteAppearanceDto ToDto(SiteAppearanceSettings entity) =>
        new(
            entity.SiteName,
            entity.LogoMode,
            entity.LogoImageUrl,
            entity.LogoIcon,
            entity.LogoText,
            entity.LoginTagline,
            entity.LoginCoverImageUrl,
            entity.PrimaryAccentColor,
            entity.HeaderBg,
            entity.HeaderColor,
            entity.TopMenuBg,
            entity.TopMenuLinkColor,
            entity.TopMenuLinkHoverBg,
            entity.TopMenuLinkHoverColor,
            entity.TopMenuLinkActiveBg,
            entity.TopMenuLinkActiveColor,
            entity.SidebarBg,
            entity.SidebarLinkColor,
            entity.SidebarLinkHoverColor,
            entity.SidebarActiveBg,
            entity.SidebarActiveColor,
            entity.SidebarIconColor,
            entity.SidebarSubmenuBg,
            entity.SidebarSectionColor,
            entity.SurfaceBg,
            entity.SurfaceSecondaryBg,
            entity.SurfaceTextColor,
            entity.BorderColor,
            entity.DropdownBg,
            entity.ModalBg,
            entity.SecondaryButtonBg,
            entity.SecondaryButtonTextColor,
            entity.SecondaryButtonBorderColor,
            entity.SecondaryButtonHoverBg,
            entity.SecondaryButtonHoverTextColor);

    private static string? Validate(UpdateSiteAppearanceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SiteName)) return "Site name is required.";
        if (string.IsNullOrWhiteSpace(request.LogoText)) return "Brand text is required.";
        if (request.LogoMode is not ("image" or "icon")) return "Logo mode must be 'image' or 'icon'.";

        foreach (var color in new[]
                 {
                     request.PrimaryAccentColor,
                     request.HeaderBg,
                     request.HeaderColor,
                     request.TopMenuBg,
                     request.TopMenuLinkColor,
                     request.TopMenuLinkHoverBg,
                     request.TopMenuLinkHoverColor,
                     request.TopMenuLinkActiveBg,
                     request.TopMenuLinkActiveColor,
                     request.SidebarBg,
                     request.SidebarLinkColor,
                     request.SidebarLinkHoverColor,
                     request.SidebarActiveBg,
                     request.SidebarActiveColor,
                     request.SidebarIconColor,
                     request.SidebarSubmenuBg,
                     request.SidebarSectionColor,
                     request.SurfaceBg,
                     request.SurfaceSecondaryBg,
                     request.SurfaceTextColor,
                     request.BorderColor,
                     request.DropdownBg,
                     request.ModalBg,
                     request.SecondaryButtonBg,
                     request.SecondaryButtonTextColor,
                     request.SecondaryButtonBorderColor,
                     request.SecondaryButtonHoverBg,
                     request.SecondaryButtonHoverTextColor
                 })
        {
            if (!HexRegex().IsMatch(color.Trim()))
            {
                return $"Invalid hex color: {color}";
            }
        }

        return null;
    }

    private static string NormalizeColor(string value) => value.Trim().ToLowerInvariant();

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static Guid GetActorId(HttpContext http)
    {
        var raw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }

    [GeneratedRegex("^#([0-9a-fA-F]{6}|[0-9a-fA-F]{3})$")]
    private static partial Regex HexRegex();
}

public sealed record SiteAppearanceDto(
    string SiteName,
    string LogoMode,
    string? LogoImageUrl,
    string? LogoIcon,
    string LogoText,
    string? LoginTagline,
    string? LoginCoverImageUrl,
    string PrimaryAccentColor,
    string HeaderBg,
    string HeaderColor,
    string TopMenuBg,
    string TopMenuLinkColor,
    string TopMenuLinkHoverBg,
    string TopMenuLinkHoverColor,
    string TopMenuLinkActiveBg,
    string TopMenuLinkActiveColor,
    string SidebarBg,
    string SidebarLinkColor,
    string SidebarLinkHoverColor,
    string SidebarActiveBg,
    string SidebarActiveColor,
    string SidebarIconColor,
    string SidebarSubmenuBg,
    string SidebarSectionColor,
    string SurfaceBg,
    string SurfaceSecondaryBg,
    string SurfaceTextColor,
    string BorderColor,
    string DropdownBg,
    string ModalBg,
    string SecondaryButtonBg,
    string SecondaryButtonTextColor,
    string SecondaryButtonBorderColor,
    string SecondaryButtonHoverBg,
    string SecondaryButtonHoverTextColor);

public sealed record UpdateSiteAppearanceRequest(
    string SiteName,
    string LogoMode,
    string? LogoImageUrl,
    string? LogoIcon,
    string LogoText,
    string? LoginTagline,
    string? LoginCoverImageUrl,
    string PrimaryAccentColor,
    string HeaderBg,
    string HeaderColor,
    string TopMenuBg,
    string TopMenuLinkColor,
    string TopMenuLinkHoverBg,
    string TopMenuLinkHoverColor,
    string TopMenuLinkActiveBg,
    string TopMenuLinkActiveColor,
    string SidebarBg,
    string SidebarLinkColor,
    string SidebarLinkHoverColor,
    string SidebarActiveBg,
    string SidebarActiveColor,
    string SidebarIconColor,
    string SidebarSubmenuBg,
    string SidebarSectionColor,
    string SurfaceBg,
    string SurfaceSecondaryBg,
    string SurfaceTextColor,
    string BorderColor,
    string DropdownBg,
    string ModalBg,
    string SecondaryButtonBg,
    string SecondaryButtonTextColor,
    string SecondaryButtonBorderColor,
    string SecondaryButtonHoverBg,
    string SecondaryButtonHoverTextColor);
