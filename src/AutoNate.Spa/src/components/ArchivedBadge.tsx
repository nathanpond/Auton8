import { Badge } from "@mantine/core";

// Non-colour cue for an archived row.
//
// `.row-archived td` dims the text, and dimming was the *only* signal: a
// screen-reader user got nothing at all, and a colour-deficient user saw a
// lightness shift they had no reason to read as "archived" (WCAG 1.4.1,
// 508 §502 — #16). A short text badge carries the same meaning through
// three independent channels: the word itself, the outline shape, and colour.
export function ArchivedBadge() {
  return (
    <Badge size="xs" variant="outline" color="gray" ml="xs" style={{ verticalAlign: "middle" }}>
      Archived
    </Badge>
  );
}
