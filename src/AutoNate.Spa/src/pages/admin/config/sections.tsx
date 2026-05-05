import SiteSettingsForm from "./SiteSettingsForm";

type StubProps = {
  title: string;
  blurb: string;
};

function Stub({ title, blurb }: StubProps) {
  return (
    <>
      <div className="page-head">
        <h1 className="page-header mb-1">{title}</h1>
        <p className="page-head-copy">{blurb}</p>
      </div>

      <div className="panel panel-inverse">
        <div className="panel-body text-muted">
          <i className="fa fa-screwdriver-wrench me-2" />
          This section is a stub. Functionality coming soon.
        </div>
      </div>
    </>
  );
}

export function ConfigIndex() {
  return (
    <>
      <div className="page-head">
        <h1 className="page-header mb-1">Site Configuration</h1>
        <p className="page-head-copy">
          Manage sitewide settings and security from a single place. Choose a
          section from the navigation on the left to get started.
        </p>
      </div>

      <div className="panel panel-inverse">
        <div className="panel-body text-muted">
          Select a category on the left to begin.
        </div>
      </div>
    </>
  );
}

export function SitewideGeneral() {
  return (
    <SiteSettingsForm
      group="general"
      title="General"
      blurb="Core sitewide settings and feature flags. Changes apply across the application."
    />
  );
}

export function SitewideFeatures() {
  return (
    <SiteSettingsForm
      group="features"
      title="Features"
      blurb="Toggle optional features and modules across the application."
    />
  );
}

export function SitewideAppearance() {
  return (
    <Stub
      title="Appearance"
      blurb="Customize the look and feel: theme, branding, colors, and logos."
    />
  );
}

export function SitewideExternalConnections() {
  return (
    <Stub
      title="External Connections"
      blurb="Configure integrations with external systems such as identity providers, message buses, and APIs."
    />
  );
}

export function SecurityManageUsers() {
  return (
    <Stub
      title="Manage Users"
      blurb="Create, update, and disable user accounts."
    />
  );
}

export function SecurityManageGroups() {
  return (
    <Stub
      title="Manage Groups"
      blurb="Organize users into groups for easier permission management."
    />
  );
}

export function SecurityManageRoles() {
  return (
    <Stub
      title="Manage Roles"
      blurb="Define named roles you can attach permissions to."
    />
  );
}

export function SecuritySetPermissions() {
  return (
    <Stub
      title="Set Permissions"
      blurb="Assign permissions to roles and configure their scopes."
    />
  );
}

export function SecurityPermissionChecker() {
  return (
    <Stub
      title="Permission Checker"
      blurb="Inspect why a user does or does not have a given permission."
    />
  );
}

export function FormsFormMappings() {
  return (
    <Stub
      title="Form Mappings"
      blurb="Map forms to record types and fields."
    />
  );
}
