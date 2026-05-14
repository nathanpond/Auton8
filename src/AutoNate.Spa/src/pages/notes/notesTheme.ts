// Color + spacing tokens for the Notes page chrome. Pulled verbatim from the
// design bundle (notes/shell.jsx) so the visual output matches the prototype
// regardless of what the live Mantine theme is set to. Kept local to /notes
// because the rest of the SPA already follows the global Mantine palette.

export const notesTheme = {
  accent: "#00acac",          // teal
  primary: "#348fe2",         // blue
  dark: "#2d353c",            // gray-800
  darkHeader: "#20252a",
  border: "#ced4da",          // gray-400
  panelBg: "#ffffff",
  bodyBg: "#dee2e6",          // gray-300
  muted: "#6c757d",
  warning: "#f59c1a",
  danger: "#ff5b57",
  info: "#49b6d6",
  green: "#32a932",
  purple: "#7950f2",
  hover: "#f6f8fa",
  selected: "#eef5fd",
  rowHover: "#eef0f2"
} as const;

// Note-kind metadata. Backend stores `noteKind` as 'richtext' | 'drawing' |
// 'diagram' (per design D6 in the back-end plan). The design prototype used
// 'visual' | 'napkin' | 'diagram' — mapping is one-to-one and the labels
// below are what we render in the UI.
export type WireNoteKind = "richtext" | "drawing" | "diagram";

export const NOTE_KIND_META: Record<
  WireNoteKind,
  { icon: string; label: string; tech: string; description: string; color: string }
> = {
  richtext: {
    icon: "fa-pen-ruler",
    label: "Visual Text",
    tech: "Mantine · Tiptap",
    description: "Rich text editor with formatting, tables, and checklists.",
    color: "#348fe2"
  },
  drawing: {
    icon: "fa-pen-nib",
    label: "Napkin",
    tech: "Excalidraw",
    description: "Free-form sketch surface — perfect for whiteboard thinking.",
    color: "#f59c1a"
  },
  diagram: {
    icon: "fa-diagram-project",
    label: "Diagram",
    tech: "Draw.io",
    description: "Flowchart canvas with shapes, swimlanes, and connectors.",
    color: "#7950f2"
  }
};

// Default cabinet color palette — the back-end stores `icon` (a FA key) on
// each cabinet but not a color. We deterministically derive the accent from
// the cabinet id so colors stay stable across reloads without an extra DB
// column. The mapping uses the prototype's six accent colors in rotation.
const CABINET_PALETTE = [
  "#348fe2", // blue
  "#00acac", // teal
  "#7950f2", // purple
  "#ff5b57", // red
  "#f59c1a", // orange
  "#49b6d6"  // cyan
];

export function cabinetColorFor(id: string): string {
  let h = 0;
  for (let i = 0; i < id.length; i++) {
    h = (h * 31 + id.charCodeAt(i)) >>> 0;
  }
  return CABINET_PALETTE[h % CABINET_PALETTE.length];
}

export function defaultCabinetIcon(): string {
  return "fa-folder";
}

// "MB" / "HD" style avatar initials from a project name.
export function projectInitials(name: string): string {
  const words = name.trim().split(/\s+/).slice(0, 2);
  if (words.length === 0) return "?";
  if (words.length === 1) return words[0].slice(0, 2).toUpperCase();
  return (words[0][0] + words[1][0]).toUpperCase();
}
