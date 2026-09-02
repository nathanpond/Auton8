import { useEffect, useMemo, useState } from "react";
import { AxiosError } from "axios";
import {
  Alert,
  Box,
  Button,
  ColorInput,
  Group,
  Paper,
  Radio,
  SimpleGrid,
  Stack,
  Text,
  TextInput,
  Title
} from "@mantine/core";
import SiteBrand from "@/components/SiteBrand";
import {
  useAdminSiteAppearance,
  useUpdateSiteAppearance
} from "@/hooks/useSiteAppearance";
import {
  DEFAULT_SITE_APPEARANCE,
  areSiteAppearancesEqual,
  checkContrastWarnings,
  coerceSiteAppearance,
  normalizeHex,
  toUpdateSiteAppearanceRequest,
  validateSiteAppearance
} from "@/lib/siteAppearance";
import { badgeTextColor } from "@/lib/statusAppearance";
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

// Keys that drive live UI tokens are surfaced. The `sidebar*` fields paint
// the Site Configuration left sidenav (ConfigLayout.css) via the --app-sidebar-*
// bridge vars. The remaining ColorAdmin-era fields (headerBg/headerColor,
// secondaryButton*, dropdownBg, modalBg, sidebarSubmenuBg) keep their saved
// values in the database but aren't editable here — they don't paint anything
// in the current Mantine shell.
type ColorFieldKey =
  | "primaryAccentColor"
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
  | "sidebarSectionColor"
  | "sidebarIconColor"
  | "surfaceBg"
  | "surfaceSecondaryBg"
  | "surfaceTextColor"
  | "surfaceDimmedColor"
  | "borderColor";

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
  { key: "siteName", label: "Site name", placeholder: "Auton8" },
  { key: "logoText", label: "Brand text", placeholder: "Auton8" },
  { key: "logoIcon", label: "Logo icon class", placeholder: "fa fa-robot", optional: true },
  {
    key: "logoImageUrl",
    label: "Logo image URL/path",
    placeholder: "/assets/img/logo.png",
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
    placeholder: "/assets/img/login-bg/space.jpg",
    optional: true
  }
];

const TOP_BAR_FIELDS: ColorFieldConfig[] = [
  { key: "topMenuBg", label: "Background" },
  { key: "topMenuLinkColor", label: "Text & icon color" },
  { key: "topMenuLinkHoverBg", label: "Hover background" },
  { key: "topMenuLinkHoverColor", label: "Hover text color" },
  { key: "topMenuLinkActiveBg", label: "Active background" },
  { key: "topMenuLinkActiveColor", label: "Active text color" }
];

const SURFACE_FIELDS: ColorFieldConfig[] = [
  { key: "surfaceBg", label: "Page background" },
  { key: "surfaceSecondaryBg", label: "Secondary surface" },
  { key: "surfaceTextColor", label: "Body text" },
  { key: "surfaceDimmedColor", label: "Secondary text" },
  { key: "borderColor", label: "Border color" }
];

const SIDEBAR_FIELDS: ColorFieldConfig[] = [
  { key: "sidebarBg", label: "Background" },
  { key: "sidebarLinkColor", label: "Item text" },
  { key: "sidebarLinkHoverColor", label: "Item hover text" },
  { key: "sidebarActiveBg", label: "Active item background" },
  { key: "sidebarActiveColor", label: "Active item text" },
  { key: "sidebarSectionColor", label: "Group label" },
  { key: "sidebarIconColor", label: "Group icon" }
];

const SAVED_MESSAGE = "Appearance settings saved.";

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
  // WCAG 1.4.3 / 1.4.11 advisory: surface low-contrast pairs to the admin
  // but do NOT block save — admins may have a legitimate brand-override or
  // debug reason to ship a sub-threshold value temporarily.
  const contrastWarnings = useMemo(() => checkContrastWarnings(currentDraft), [currentDraft]);

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
      setSaveMessage(SAVED_MESSAGE);
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
    <Box py="md">
      <Stack gap="lg">
        <Group justify="space-between" align="flex-start" wrap="wrap" gap="md">
          <Stack gap={4}>
            <Title order={1}>Appearance</Title>
            <Text size="sm" c="dimmed">
              Customize branding, navigation colors, and core surfaces with a live preview.
            </Text>
          </Stack>
          <Group gap="xs" wrap="wrap">
            <Button
              variant="default"
              onClick={resetToSaved}
              disabled={isLoading || updateAppearance.isPending}
            >
              Reset to saved
            </Button>
            <Button
              variant="default"
              onClick={resetToDefaults}
              disabled={updateAppearance.isPending}
            >
              Reset to defaults
            </Button>
            <Button
              onClick={() => void handleSave()}
              loading={updateAppearance.isPending}
              disabled={isLoading || !isDirty}
            >
              Save changes
            </Button>
          </Group>
        </Group>

        {saveMessage && (
          <Alert color={saveMessage === SAVED_MESSAGE ? "green" : "red"} variant="light">
            {saveMessage}
          </Alert>
        )}

        {contrastWarnings.length > 0 && (
          <Alert color="yellow" variant="light" title="Accessibility advisory">
            <Text size="sm" mb={6}>
              The current theme has {contrastWarnings.length} color
              {contrastWarnings.length === 1 ? " pair" : " pairs"} below the WCAG 2.0 AA contrast
              floor. You can still save — this is advisory, not a block — but the marked surfaces
              will be hard to read for users with low vision.
            </Text>
            <Stack gap={4} component="ul" style={{ paddingLeft: 18, margin: 0 }}>
              {contrastWarnings.map((w) => (
                <li key={w.pairLabel}>
                  <Text size="sm" span>
                    <strong>{w.pairLabel}</strong>: {w.ratio.toFixed(2)}:1 (needs {w.required.toFixed(1)}:1
                    {w.reason === "text" ? " for text" : " for UI components"})
                  </Text>
                </li>
              ))}
            </Stack>
          </Alert>
        )}

        <Section title="Branding">
          <Stack gap="md">
            <Radio.Group
              label="Logo mode"
              value={currentDraft.logoMode}
              onChange={(value) => updateField("logoMode", value as SiteAppearance["logoMode"])}
            >
              <Group gap="md" mt="xs">
                <Radio value="icon" label="Icon + text" />
                <Radio value="image" label="Image logo" />
              </Group>
            </Radio.Group>
            <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }} spacing="md">
              {BRANDING_FIELDS.map((field) => (
                <TextInput
                  key={field.key}
                  label={
                    field.optional ? (
                      <span>
                        {field.label}{" "}
                        <Text component="span" c="dimmed" size="xs">
                          (optional)
                        </Text>
                      </span>
                    ) : (
                      field.label
                    )
                  }
                  value={currentDraft[field.key] ?? ""}
                  placeholder={field.placeholder}
                  error={errors[field.key]}
                  onChange={(event) => updateField(field.key, event.currentTarget.value)}
                />
              ))}
            </SimpleGrid>
          </Stack>
        </Section>

        <Section title="Brand Color">
          <Box maw={360}>
            <ColorInput
              label="Primary accent"
              description="Drives Mantine's brand palette and the active states in the top bar."
              format="hex"
              withEyeDropper
              // Mantine's eyedropper renders an icon-only button with no
              // accessible name — 19 of them on this page, all announced as
              // just "button". Naming it after the field it samples keeps
              // them distinguishable in a screen reader's control list.
              eyeDropperButtonProps={{ "aria-label": "Pick primary accent color from screen" }}
              value={currentDraft.primaryAccentColor}
              error={errors.primaryAccentColor}
              onChange={(value) => updateField("primaryAccentColor", value)}
              popoverProps={{ withinPortal: true, position: "bottom-start" }}
            />
          </Box>
        </Section>

        <ColorSection
          title="Top Bar"
          description="Controls the dark navigation bar at the top of every signed-in page."
          fields={TOP_BAR_FIELDS}
          values={currentDraft}
          errors={errors}
          onChange={updateField}
        />

        <ColorSection
          title="Surfaces"
          description="Mirrored into Mantine's body / text / border tokens via the SiteAppearance bridge."
          fields={SURFACE_FIELDS}
          values={currentDraft}
          errors={errors}
          onChange={updateField}
        />

        <ColorSection
          title="Site Configuration Sidebar"
          description="Paints the left navigation panel inside Site Configuration pages."
          fields={SIDEBAR_FIELDS}
          values={currentDraft}
          errors={errors}
          onChange={updateField}
        />

        <Section title="Live Preview">
          <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md">
            <TopMenuPreview appearance={currentDraft} />
            <SurfacePreview appearance={currentDraft} />
            <SidebarPreview appearance={currentDraft} />
            <LoginPreview appearance={currentDraft} />
          </SimpleGrid>
        </Section>
      </Stack>
    </Box>
  );
}

function Section({
  title,
  description,
  children
}: {
  title: string;
  description?: string;
  children: React.ReactNode;
}) {
  return (
    <Paper withBorder radius="md" p="md">
      <Stack gap="sm">
        <Stack gap={2}>
          <Title order={4}>{title}</Title>
          {description && (
            <Text size="xs" c="dimmed">
              {description}
            </Text>
          )}
        </Stack>
        {children}
      </Stack>
    </Paper>
  );
}

function ColorSection({
  title,
  description,
  fields,
  values,
  errors,
  onChange
}: {
  title: string;
  description?: string;
  fields: ColorFieldConfig[];
  values: SiteAppearance;
  errors: SiteAppearanceErrors;
  onChange: <K extends keyof SiteAppearance>(key: K, value: SiteAppearance[K]) => void;
}) {
  return (
    <Section title={title} description={description}>
      <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }} spacing="md">
        {fields.map((field) => (
          <ColorInput
            key={field.key}
            label={field.label}
            format="hex"
            withEyeDropper
            eyeDropperButtonProps={{ "aria-label": `Pick ${field.label} color from screen` }}
            value={values[field.key]}
            error={errors[field.key]}
            onChange={(value) => onChange(field.key, value)}
            popoverProps={{ withinPortal: true, position: "bottom-start" }}
          />
        ))}
      </SimpleGrid>
    </Section>
  );
}

function TopMenuPreview({ appearance }: { appearance: SiteAppearance }) {
  return (
    <PreviewCard>
      <Box
        className="site-appearance-preview-header"
        style={{ background: appearance.topMenuBg, color: appearance.topMenuLinkColor }}
      >
        <Group justify="space-between" align="center" wrap="nowrap" gap="md">
          <SiteBrand
            appearance={appearance}
            style={{ display: "inline-flex", alignItems: "center", gap: 8, fontWeight: 700 }}
            iconClassName=""
            textClassName=""
            imageClassName="site-appearance-brand-image"
          />
          <Group gap="xs" wrap="wrap">
            <PreviewMenuChip
              label="Home"
              bg={appearance.topMenuLinkHoverBg}
              color={appearance.topMenuLinkHoverColor}
            />
            <PreviewMenuChip
              label="Records"
              bg={appearance.topMenuLinkActiveBg}
              color={appearance.topMenuLinkActiveColor}
            />
            <PreviewMenuChip label="Workflows" bg="transparent" color={appearance.topMenuLinkColor} />
          </Group>
        </Group>
      </Box>
      <Box className="site-appearance-preview-body">
        <Text size="xs" c="dimmed" mb="xs">
          Buttons and badges
        </Text>
        <Group gap="xs" wrap="wrap">
          <Button
            style={{
              background: appearance.primaryAccentColor,
              borderColor: appearance.primaryAccentColor,
              color: badgeTextColor(appearance.primaryAccentColor)
            }}
          >
            Primary action
          </Button>
          <Button variant="default">Secondary action</Button>
          <Box
            component="span"
            px="md"
            py={6}
            style={{
              borderRadius: 999,
              fontSize: 12,
              background: appearance.primaryAccentColor,
              color: badgeTextColor(appearance.primaryAccentColor)
            }}
          >
            Accent badge
          </Box>
        </Group>
      </Box>
    </PreviewCard>
  );
}

function SurfacePreview({ appearance }: { appearance: SiteAppearance }) {
  return (
    <PreviewCard>
      <Box className="site-appearance-preview-body">
        <Text size="xs" c="dimmed" mb="xs">
          Surface colors
        </Text>
        <Stack gap="md">
          <SurfaceSwatch
            label="Primary panel surface"
            background={appearance.surfaceBg}
            color={appearance.surfaceTextColor}
            border={appearance.borderColor}
          />
          <SurfaceSwatch
            label="Secondary surface"
            background={appearance.surfaceSecondaryBg}
            color={appearance.surfaceTextColor}
            border={appearance.borderColor}
          />
          <SurfaceSwatch
            label="Dropdown / modal background preview"
            background={appearance.dropdownBg}
            color={appearance.surfaceTextColor}
            border={appearance.borderColor}
          />
        </Stack>
      </Box>
    </PreviewCard>
  );
}

function SurfaceSwatch({
  label,
  background,
  color,
  border
}: {
  label: string;
  background: string;
  color: string;
  border: string;
}) {
  return (
    <Box
      p="md"
      style={{
        borderRadius: 12,
        background,
        color,
        border: `1px solid ${border}`
      }}
    >
      {label}
    </Box>
  );
}

function SidebarPreview({ appearance }: { appearance: SiteAppearance }) {
  const itemStyle: React.CSSProperties = {
    display: "flex",
    alignItems: "center",
    gap: 8,
    padding: "6px 12px 6px 28px",
    color: appearance.sidebarLinkColor,
    fontSize: 13,
    borderLeft: "3px solid transparent"
  };
  const activeItemStyle: React.CSSProperties = {
    ...itemStyle,
    background: appearance.sidebarActiveBg,
    color: appearance.sidebarActiveColor,
    borderLeftColor: appearance.primaryAccentColor
  };
  const groupHeaderStyle: React.CSSProperties = {
    display: "flex",
    alignItems: "center",
    gap: 8,
    padding: "8px 14px",
    color: appearance.sidebarSectionColor,
    fontSize: 11,
    fontWeight: 600,
    textTransform: "uppercase",
    letterSpacing: "0.06em"
  };
  return (
    <PreviewCard>
      <Box
        style={{
          background: appearance.sidebarBg,
          color: appearance.sidebarLinkColor,
          minHeight: 220,
          paddingBottom: 8
        }}
      >
        <Box
          style={{
            padding: "12px 14px",
            fontWeight: 600,
            fontSize: 13,
            color: appearance.sidebarActiveColor,
            borderBottom: "1px solid rgba(255,255,255,0.06)"
          }}
        >
          <i className="fa fa-sliders" aria-hidden style={{ marginRight: 8 }} />
          Site Configuration
        </Box>
        <Box style={groupHeaderStyle}>
          <i
            className="fa fa-palette"
            aria-hidden
            style={{ color: appearance.sidebarIconColor }}
          />
          <span style={{ flex: 1 }}>Appearance</span>
          <i
            className="fa fa-chevron-down"
            aria-hidden
            style={{ color: appearance.sidebarIconColor, fontSize: 10 }}
          />
        </Box>
        <Box style={activeItemStyle}>
          <i className="fa fa-paintbrush" aria-hidden />
          <span>Appearance</span>
        </Box>
        <Box style={itemStyle}>
          <i className="fa fa-circle-half-stroke" aria-hidden />
          <span>Status colors</span>
        </Box>
        <Box style={groupHeaderStyle}>
          <i
            className="fa fa-screwdriver-wrench"
            aria-hidden
            style={{ color: appearance.sidebarIconColor }}
          />
          <span style={{ flex: 1 }}>System</span>
        </Box>
      </Box>
    </PreviewCard>
  );
}

function LoginPreview({ appearance }: { appearance: SiteAppearance }) {
  return (
    <PreviewCard>
      <Box
        className="site-appearance-login-cover"
        style={{
          backgroundColor: appearance.headerBg,
          backgroundImage: appearance.loginCoverImageUrl
            ? `url("${appearance.loginCoverImageUrl}")`
            : undefined,
          display: "flex",
          alignItems: "flex-end",
          padding: 16
        }}
      >
        <Box className="site-appearance-login-brand" style={{ color: "#fff" }}>
          <SiteBrand
            appearance={appearance}
            style={{ display: "inline-flex", alignItems: "center", gap: 8, fontSize: 18, fontWeight: 700 }}
            iconClassName=""
            textClassName=""
            imageClassName="site-appearance-brand-image"
          />
          <Text size="xs" mt="xs">
            {appearance.loginTagline || "Optional login subtitle"}
          </Text>
        </Box>
      </Box>
      <Box className="site-appearance-preview-body">
        <Box
          p="md"
          style={{
            borderRadius: 12,
            background: appearance.modalBg,
            color: appearance.surfaceTextColor,
            border: `1px solid ${appearance.borderColor}`
          }}
        >
          Login card surface
        </Box>
      </Box>
    </PreviewCard>
  );
}

function PreviewCard({
  children,
  style
}: {
  children: React.ReactNode;
  style?: React.CSSProperties;
}) {
  return (
    <Paper
      withBorder
      radius="md"
      style={{ overflow: "hidden", minHeight: "100%", ...style }}
    >
      {children}
    </Paper>
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
    <Box
      component="span"
      px="md"
      py={4}
      style={{
        borderRadius: 999,
        background: normalizedBg,
        color,
        fontSize: 12
      }}
    >
      {label}
    </Box>
  );
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
