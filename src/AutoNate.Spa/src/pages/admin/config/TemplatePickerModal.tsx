import { useMemo } from "react";
import type { PageTemplateInfo } from "@/api/pageTemplates";
import { CardItem, CardPickerModal } from "@/components/picker/CardPickerModal";

// Thin wrapper around the generic CardPickerModal. Keeps the same call
// signature consumers used before the extraction so existing call sites
// (MenuItemEditModal, etc.) don't need to change.
type Props = {
  templates: PageTemplateInfo[];
  selectedKey: string | null;
  onSelect: (template: PageTemplateInfo) => void;
  onCancel: () => void;
};

export default function TemplatePickerModal({
  templates,
  selectedKey,
  onSelect,
  onCancel
}: Props) {
  const byKey = useMemo(() => {
    const m = new Map<string, PageTemplateInfo>();
    for (const t of templates) m.set(t.key, t);
    return m;
  }, [templates]);

  const items = useMemo<CardItem[]>(
    () =>
      templates.map((t) => ({
        key: t.key,
        name: t.name,
        description: t.description ?? null,
        category: t.category ?? null,
        thumbnail: t.thumbnailUrl ?? null
      })),
    [templates]
  );

  return (
    <CardPickerModal
      items={items}
      selectedKey={selectedKey}
      title="Choose a page template"
      subtitle="Pick a built-in starter to mount on this menu item."
      searchPlaceholder="Search templates"
      confirmLabel="Use template"
      onSelect={(item) => {
        const original = byKey.get(item.key);
        if (original) onSelect(original);
      }}
      onCancel={onCancel}
    />
  );
}
