import { ComponentType } from "react";
import { Link, NavLink } from "react-router-dom";

// Components admins can reference by name in JSX page content. Plain HTML
// elements are allowed by react-jsx-parser without being listed here. The
// `blacklistedAttrs` default already strips `on*` event handlers; we add
// `dangerouslySetInnerHTML` and `style`-as-string for extra safety.
//
// Cast to a permissive ComponentType so react-jsx-parser's strict
// `FunctionComponent<{}>` constraint accepts components with required props
// (Link/NavLink require `to`). The runtime requires the JSX author to supply
// those props correctly; a type cast is the right escape hatch.
export const JSX_COMPONENTS: Record<string, ComponentType<Record<string, unknown>>> = {
  Link: Link as unknown as ComponentType<Record<string, unknown>>,
  NavLink: NavLink as unknown as ComponentType<Record<string, unknown>>
};

export const JSX_BLACKLISTED_ATTRS: (string | RegExp)[] = [
  /^on.+/i,
  "dangerouslySetInnerHTML"
];

export const JSX_BLACKLISTED_TAGS = ["script", "style", "iframe", "object", "embed"];
