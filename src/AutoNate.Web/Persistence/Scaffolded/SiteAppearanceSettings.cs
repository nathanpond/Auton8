using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class SiteAppearanceSettings
{
    public Guid Id { get; set; }

    public string SiteName { get; set; } = null!;

    public string LogoMode { get; set; } = null!;

    public string? LogoImageUrl { get; set; }

    public string? LogoIcon { get; set; }

    public string LogoText { get; set; } = null!;

    public string? LoginTagline { get; set; }

    public string? LoginCoverImageUrl { get; set; }

    public string PrimaryAccentColor { get; set; } = null!;

    public string HeaderBg { get; set; } = null!;

    public string HeaderColor { get; set; } = null!;

    public string TopMenuBg { get; set; } = null!;

    public string TopMenuLinkColor { get; set; } = null!;

    public string TopMenuLinkHoverBg { get; set; } = null!;

    public string TopMenuLinkHoverColor { get; set; } = null!;

    public string TopMenuLinkActiveBg { get; set; } = null!;

    public string TopMenuLinkActiveColor { get; set; } = null!;

    public string SidebarBg { get; set; } = null!;

    public string SidebarLinkColor { get; set; } = null!;

    public string SidebarLinkHoverColor { get; set; } = null!;

    public string SidebarActiveBg { get; set; } = null!;

    public string SidebarActiveColor { get; set; } = null!;

    public string SidebarIconColor { get; set; } = null!;

    public string SidebarSubmenuBg { get; set; } = null!;

    public string SidebarSectionColor { get; set; } = null!;

    public string SurfaceBg { get; set; } = null!;

    public string SurfaceSecondaryBg { get; set; } = null!;

    public string SurfaceTextColor { get; set; } = null!;

    public string BorderColor { get; set; } = null!;

    public string DropdownBg { get; set; } = null!;

    public string ModalBg { get; set; } = null!;

    public string SecondaryButtonBg { get; set; } = null!;

    public string SecondaryButtonTextColor { get; set; } = null!;

    public string SecondaryButtonBorderColor { get; set; } = null!;

    public string SecondaryButtonHoverBg { get; set; } = null!;

    public string SecondaryButtonHoverTextColor { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid UpdatedBy { get; set; }
}
