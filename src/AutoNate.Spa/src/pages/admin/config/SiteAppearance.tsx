import { useEffect, useMemo, useState } from "react";
import { AxiosError } from "axios";
import ColorPicker from "@/components/ColorPicker";
import SiteBrand from "@/components/SiteBrand";
import {
  useAdminSiteAppearance,
  useUpdateSiteAppearance
} from "@/hooks/useSiteAppearance";
import {
  DEFAULT_SITE_APPEARANCE,
  areSiteAppearancesEqual,
  badgeTextColor,
  coerceSiteAppearance,
  normalizeHex,
  toUpdateSiteAppearanceRequest,
  validateSiteAppearance
} from "@/lib/siteAppearance";
import { useSiteAppearance } from "@/providers/SiteAppearanceProvider";
import { SiteAppearance } from "@/types/siteAppearance";
import "./SiteAppearance.css";

type SiteAppearanceErrors = Partial<Record<keyof SiteAppearance, string>>;

type BrandingFieldKey =
  | "siteName"
  | "logoText"
  | "logoIcon"
  | "logoImageUrl"
  | "loginTagline"
  | "loginCoverImageUrl";

type ColorFieldKey =
  | "primaryAccentColor"
  | "headerBg"
  | "headerColor"
  | "topMenuBg"
  | "topMenuLinkColor"
  | "topMenuLinkHoverBg"
  | "topMenuLinkHoverColor"
  | "topMenuLinkActiveBg"
  | "topMenuLinkActiveColor"
  | "sidebarBg"
  | "sidebarLinkColor"
  | "sidebarLinkHoverColor"
  | "sidebarActiveBg"
  | "sidebarActiveColor"
  | "sidebarIconColor"
  | "sidebarSubmenuBg"
  | "sidebarSectionColor"
  | "surfaceBg"
  | "surfaceSecondaryBg"
  | "surfaceTextColor"
  | "borderColor"
  | "dropdownBg"
  | "modalBg"
  | "secondaryButtonBg"
  | "secondaryButtonTextColor"
  | "secondaryButtonBorderColor"
  | "secondaryButtonHoverBg"
  | "secondaryButtonHoverTextColor";

type TextFieldConfig = {
  key: BrandingFieldKey;
  label: string;
  placeholder?: string;
  optional?: boolean;
};

type ColorFieldConfig = {
  key: ColorFieldKey;
  label: string;
};

const BRANDING_FIELDS: TextFieldConfig[] = [
  { key: "siteName", label: "Site name", placeholder: "Auto Nate" },
  { key: "logoText", label: "Brand text", placeholder: "Auto Nate" },
  { key: "logoIcon", label: "Logo icon class", placeholder: "fa fa-robot", optional: true },
  {
    key: "logoImageUrl",
    label: "Logo image URL/path",
    placeholder: "/spa/assets/img/logo.png",
    optional: true
  },
  {
    key: "loginTagline",
    label: "Login subtitle text",
    placeholder: "Sign in to continue to the automation dashboard",
    optional: true
  },
  {
    key: "loginCoverImageUrl",
    label: "Login cover image URL/path",
    placeholder: "/spa/assets/img/login-bg/login-bg-17.jpg",
    optional: true
  }
];

const HEADER_FIELDS: ColorFieldConfig[] = [
  { key: "headerBg", label: "Header background" },
  { key: "headerColor", label: "Header text" },
  { key: "topMenuBg", label: "Top menu background" },
  { key: "topMenuLinkColor", label: "Top menu link color" },
  { key: "topMenuLinkHoverBg", label: "Top menu hover background" },
  { key: "topMenuLinkHoverColor", label: "Top menu hover color" },
  { key: "topMenuLinkActiveBg", label: "Top menu active background" },
  { key: "topMenuLinkActiveColor", label: "Top menu active color" }
];

const SIDEBAR_FIELDS: ColorFieldConfig[] = [
  { key: "sidebarBg", label: "Sidebar background" },
  { key: "sidebarLinkColor", label: "Sidebar link color" },
  { key: "sidebarLinkHoverColor", label: "Sidebar hover color" },
  { key: "sidebarActiveBg", label: "Sidebar active background" },
  { key: "sidebarActiveColor", label: "Sidebar active color" },
  { key: "sidebarIconColor", label: "Sidebar icon color" },
  { key: "sidebarSubmenuBg", label: "Sidebar submenu background" },
  { key: "sidebarSectionColor", label: "Sidebar section text" }
];

const SURFACE_FIELDS: ColorFieldConfig[] = [
  { key: "surfaceBg", label: "Surface background" },
  { key: "surfaceSecondaryBg", label: "Secondary surface background" },
  { key: "surfaceTextColor", label: "Surface text color" },
  { key: "borderColor", label: "Border color" },
  { key: "dropdownBg", label: "Dropdown background" },
  { key: "modalBg", label: "Modal background" }
];

const SECONDARY_BUTTON_FIELDS: ColorFieldConfig[] = [
  { key: "secondaryButtonBg", label: "Secondary button background" },
  { key: "secondaryButtonTextColor", label: "Secondary button text" },
  { key: "secondaryButtonBorderColor", label: "Secondary button border" },
  { key: "secondaryButtonHoverBg", label: "Secondary button hover background" },
  { key: "secondaryButtonHoverTextColor", label: "Secondary button hover text" }
];

export default function SiteAppearancePage() {
  const { data, isLoading } = useAdminSiteAppearance();
  const updateAppearance = useUpdateSiteAppearance();
  const { clearPreviewAppearance, setPreviewAppearance } = useSiteAppearance();
  const [draft, setDraft] = useState<SiteAppearance | null>(null);
  const [errors, setErrors] = useState<SiteAppearanceErrors>({});
  const [saveMessage, setSaveMessage] = useState<string | null>(null);

  const savedAppearance = useMemo(
    () => coerceSiteAppearance(data ?? DEFAULT_SITE_APPEARANCE),
    [data]
  );

  useEffect(() => {
    setDraft(savedAppearance);
    setErrors({});
  }, [savedAppearance]);

  const hasDraft = draft !== null;
  const currentDraft = draft ?? savedAppearance;
  const isDirty = hasDraft && !areSiteAppearancesEqual(currentDraft, savedAppearance);

  useEffect(() => {
    if (!hasDraft) return;

    if (isDirty) {
      setPreviewAppearance(currentDraft);
    } else {
      clearPreviewAppearance();
    }
  }, [clearPreviewAppearance, currentDraft, hasDraft, isDirty, setPreviewAppearance]);

  useEffect(() => clearPreviewAppearance, [clearPreviewAppearance]);

  const updateField = <K extends keyof SiteAppearance>(key: K, value: SiteAppearance[K]) => {
    setDraft((current) => {
      const base = current ?? savedAppearance;
      return { ...base, [key]: value };
    });
    setSaveMessage(null);
    setErrors((current) => ({ ...current, [key]: undefined }));
  };

  const handleSave = async () => {
    const validationErrors = validateSiteAppearance(currentDraft);
    setErrors(validationErrors);
    setSaveMessage(null);

    if (Object.keys(validationErrors).length > 0) {
      return;
    }

    try {
      const saved = await updateAppearance.mutateAsync(
        toUpdateSiteAppearanceRequest(currentDraft)
      );
      const normalized = coerceSiteAppearance(saved);
      setDraft(normalized);
      clearPreviewAppearance();
      setErrors({});
      setSaveMessage("Appearance settings saved.");
    } catch (error) {
      setSaveMessage(describeError(error));
    }
  };

  const resetToSaved = () => {
    setDraft(savedAppearance);
    setErrors({});
    setSaveMessage(null);
    clearPreviewAppearance();
  };

  const resetToDefaults = () => {
    setDraft(DEFAULT_SITE_APPEARANCE);
    setErrors({});
    setSaveMessage(null);
  };

  return (
    <>
      <div className="page-head">
        <div className="d-flex flex-wrap gap-3 justify-content-between align-items-start">
          <div>
            <h1 className="page-header mb-1">Appearance</h1>
            <p className="page-head-copy mb-0">
              Customize branding, navigation colors, and core surfaces with a live preview.
            </p>
          </div>

          <div className="d-flex flex-wrap gap-2 site-appearance-actions">
            <button
              type="button"
              className="btn btn-site-secondary"
              onClick={resetToSaved}
              disabled={isLoading || updateAppearance.isPending}
            >
              Reset to saved
            </button>
            <button
              type="button"
              className="btn btn-site-secondary"
              onClick={resetToDefaults}
              disabled={updateAppearance.isPending}
            >
              Reset to defaults
            </button>
            <button
              type="button"
              className="btn btn-theme"
              onClick={() => void handleSave()}
              disabled={isLoading || updateAppearance.isPending || !isDirty}
            >
              {updateAppearance.isPending ? "Saving..." : "Save changes"}
            </button>
          </div>
        </div>
      </div>

      <div className="site-appearance-grid">
        {saveMessage && (
          <div
            className={`alert ${saveMessage === "Appearance settings saved." ? "alert-success" : "alert-danger"} mb-0`}
          >
            {saveMessage}
          </div>
        )}

        <div className="panel panel-inverse">
          <div className="panel-heading">
            <h4 className="panel-title mb-0">Branding</h4>
          </div>
          <div className="panel-body">
            <div className="mb-3">
              <div className="form-label">Logo mode</div>
              <div className="d-flex flex-wrap gap-3">
                <label className="form-check">
                  <input
                    className="form-check-input"
                    type="radio"
                    name="logoMode"
                    checked={currentDraft.logoMode === "icon"}
                    onChange={() => updateField("logoMode", "icon")}
                  />
                  <span className="form-check-label">Icon + text</span>
                </label>
                <label className="form-check">
                  <input
                    className="form-check-input"
                    type="radio"
                    name="logoMode"
                    checked={currentDraft.logoMode === "image"}
                    onChange={() => updateField("logoMode", "image")}
                  />
                  <span className="form-check-label">Image logo</span>
                </label>
              </div>
            </div>

            <div className="site-appearance-section-grid">
              {BRANDING_FIELDS.map((field) => (
                <TextInput
                  key={field.key}
                  label={field.label}
                  value={currentDraft[field.key]}
                  placeholder={field.placeholder}
                  optional={field.optional}
                  error={errors[field.key]}
                  onChange={(value) => updateField(field.key, value)}
                />
              ))}
            </div>
          </div>
        </div>

        <div className="panel panel-inverse">
          <div className="panel-heading">
            <h4 className="panel-title mb-0">Primary Theme</h4>
          </div>
          <div className="panel-body">
            <ColorField
              label="Primary accent color"
              value={currentDraft.primaryAccentColor}
              error={errors.primaryAccentColor}
              onChange={(value) => updateField("primaryAccentColor", value)}
            />
          </div>
        </div>

        <ColorSection
          title="Header / Top Menu"
          fields={HEADER_FIELDS}
          values={currentDraft}
          errors={errors}
          onChange={updateField}
        />

        <ColorSection
          title="Sidebar"
          fields={SIDEBAR_FIELDS}
          values={currentDraft}
          errors={errors}
          onChange={updateField}
        />

        <ColorSection
          title="Surfaces"
          fields={SURFACE_FIELDS}
          values={currentDraft}
          errors={errors}
          onChange={updateField}
        />

        <ColorSection
          title="Secondary Buttons"
          fields={SECONDARY_BUTTON_FIELDS}
          values={currentDraft}
          errors={errors}
          onChange={updateField}
        />

        <div className="panel panel-inverse">
          <div className="panel-heading">
            <h4 className="panel-title mb-0">Live Preview</h4>
          </div>
          <div className="panel-body">
            <div className="site-appearance-preview-grid">
              <TopMenuPreview appearance={currentDraft} />
              <SidebarPreview appearance={currentDraft} />
              <SurfacePreview appearance={currentDraft} />
              <LoginPreview appearance={currentDraft} />
            </div>
          </div>
        </div>
      </div>
    </>
  );
}

function ColorSection({
  title,
  fields,
  values,
  errors,
  onChange
}: {
  title: string;
  fields: ColorFieldConfig[];
  values: SiteAppearance;
  errors: SiteAppearanceErrors;
  onChange: <K extends keyof SiteAppearance>(key: K, value: SiteAppearance[K]) => void;
}) {
  return (
    <div className="panel panel-inverse">
      <div className="panel-heading">
        <h4 className="panel-title mb-0">{title}</h4>
      </div>
      <div className="panel-body">
        <div className="site-appearance-section-grid">
          {fields.map((field) => (
            <ColorField
              key={field.key}
              label={field.label}
              value={values[field.key]}
              error={errors[field.key]}
              onChange={(value) => onChange(field.key, value)}
            />
          ))}
        </div>
      </div>
    </div>
  );
}

function TextInput({
  label,
  value,
  placeholder,
  optional,
  error,
  onChange
}: {
  label: string;
  value: string | null;
  placeholder?: string;
  optional?: boolean;
  error?: string;
  onChange: (value: string) => void;
}) {
  return (
    <div>
      <label className="form-label">
        {label}
        {optional ? <span className="text-body text-opacity-50 ms-1">(optional)</span> : null}
      </label>
      <input
        className={`form-control ${error ? "is-invalid" : ""}`}
        value={value ?? ""}
        placeholder={placeholder}
        onChange={(event) => onChange(event.target.value)}
      />
      {error ? <div className="invalid-feedback">{error}</div> : null}
    </div>
  );
}

function ColorField({
  label,
  value,
  error,
  onChange
}: {
  label: string;
  value: string;
  error?: string;
  onChange: (value: string) => void;
}) {
  return (
    <div>
      <div className="site-appearance-swatch-label">{label}</div>
      <ColorPicker value={value} onChange={onChange} />
      {error ? <div className="text-danger small mt-1">{error}</div> : null}
    </div>
  );
}

function TopMenuPreview({ appearance }: { appearance: SiteAppearance }) {
  return (
    <div className="site-appearance-preview-card">
      <div
        className="site-appearance-preview-header d-flex justify-content-between align-items-center gap-3"
        style={{ background: appearance.topMenuBg, color: appearance.topMenuLinkColor }}
      >
        <SiteBrand
          appearance={appearance}
          className="d-inline-flex align-items-center gap-2 fw-bold"
          iconClassName="d-inline-flex align-items-center"
          textClassName="d-inline-flex align-items-center"
          imageClassName="site-appearance-brand-image"
        />
        <div className="d-flex gap-2 flex-wrap">
          <PreviewMenuChip label="Home" bg={appearance.topMenuLinkHoverBg} color={appearance.topMenuLinkHoverColor} />
          <PreviewMenuChip label="Records" bg={appearance.topMenuLinkActiveBg} color={appearance.topMenuLinkActiveColor} />
          <PreviewMenuChip label="Workflows" bg="transparent" color={appearance.topMenuLinkColor} />
        </div>
      </div>
      <div className="site-appearance-preview-body">
        <div className="small text-body text-opacity-50 mb-2">Buttons and badges</div>
        <div className="d-flex flex-wrap gap-2">
          <button
            type="button"
            className="btn"
            style={{
              background: appearance.primaryAccentColor,
              borderColor: appearance.primaryAccentColor,
              color: badgeTextColor(appearance.primaryAccentColor)
            }}
          >
            Primary action
          </button>
          <button type="button" className="btn btn-site-secondary">
            Secondary action
          </button>
          <span
            className="badge rounded-pill px-3 py-2"
            style={{
              background: appearance.primaryAccentColor,
              color: badgeTextColor(appearance.primaryAccentColor)
            }}
          >
            Accent badge
          </span>
        </div>
      </div>
    </div>
  );
}

function SidebarPreview({ appearance }: { appearance: SiteAppearance }) {
  return (
    <div className="site-appearance-preview-card site-appearance-preview-sidebar">
      <div className="site-appearance-preview-body">
        <div
          className="text-uppercase small fw-bold mb-3"
          style={{ color: appearance.sidebarSectionColor }}
        >
          Site Configuration
        </div>
        <div className="nav flex-column gap-2">
          <a href="#" className="nav-link active" onClick={preventDefault}>
            <i
              className="fa fa-palette me-2"
              style={{ color: appearance.sidebarIconColor }}
            />
            Appearance
          </a>
          <a href="#" className="nav-link" onClick={preventDefault}>
            <i
              className="fa fa-sliders me-2"
              style={{ color: appearance.sidebarIconColor }}
            />
            Status Appearance
          </a>
          <div
            className="rounded-3 p-3 mt-2"
            style={{
              background: appearance.sidebarSubmenuBg,
              border: `1px solid ${appearance.borderColor}`
            }}
          >
            <div className="small" style={{ color: appearance.sidebarLinkHoverColor }}>
              Nested submenu background
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function SurfacePreview({ appearance }: { appearance: SiteAppearance }) {
  return (
    <div className="site-appearance-preview-card">
      <div className="site-appearance-preview-body">
        <div className="small text-body text-opacity-50 mb-2">Surface colors</div>
        <div
          className="rounded-3 p-3 mb-3"
          style={{
            background: appearance.surfaceBg,
            color: appearance.surfaceTextColor,
            border: `1px solid ${appearance.borderColor}`
          }}
        >
          Primary panel surface
        </div>
        <div
          className="rounded-3 p-3 mb-3"
          style={{
            background: appearance.surfaceSecondaryBg,
            color: appearance.surfaceTextColor,
            border: `1px solid ${appearance.borderColor}`
          }}
        >
          Secondary surface
        </div>
        <div
          className="rounded-3 p-3"
          style={{
            background: appearance.dropdownBg,
            color: appearance.surfaceTextColor,
            border: `1px solid ${appearance.borderColor}`
          }}
        >
          Dropdown / modal background preview
        </div>
      </div>
    </div>
  );
}

function LoginPreview({ appearance }: { appearance: SiteAppearance }) {
  return (
    <div className="site-appearance-preview-card">
      <div
        className="site-appearance-login-cover d-flex align-items-end p-3"
        style={{
          backgroundColor: appearance.headerBg,
          backgroundImage: appearance.loginCoverImageUrl
            ? `url("${appearance.loginCoverImageUrl}")`
            : undefined
        }}
      >
        <div className="site-appearance-login-brand text-white">
          <SiteBrand
            appearance={appearance}
            className="d-inline-flex align-items-center gap-2 fs-5 fw-bold"
            iconClassName="d-inline-flex align-items-center"
            textClassName="d-inline-flex align-items-center"
            imageClassName="site-appearance-brand-image"
          />
          <div className="small mt-2">{appearance.loginTagline || "Optional login subtitle"}</div>
        </div>
      </div>
      <div className="site-appearance-preview-body">
        <div
          className="rounded-3 p-3"
          style={{
            background: appearance.modalBg,
            color: appearance.surfaceTextColor,
            border: `1px solid ${appearance.borderColor}`
          }}
        >
          Login card surface
        </div>
      </div>
    </div>
  );
}

function PreviewMenuChip({
  label,
  bg,
  color
}: {
  label: string;
  bg: string;
  color: string;
}) {
  const normalizedBg = normalizeHex(bg) ?? "transparent";
  return (
    <span
      className="rounded-pill px-3 py-1 small"
      style={{ background: normalizedBg, color }}
    >
      {label}
    </span>
  );
}

function preventDefault(event: React.MouseEvent<HTMLAnchorElement>) {
  event.preventDefault();
}

function describeError(error: unknown): string {
  if (error instanceof AxiosError) {
    return (
      (typeof error.response?.data === "string" && error.response.data) ||
      error.message ||
      "Unable to save appearance settings."
    );
  }

  if (error instanceof Error) {
    return error.message;
  }

  return "Unable to save appearance settings.";
}
