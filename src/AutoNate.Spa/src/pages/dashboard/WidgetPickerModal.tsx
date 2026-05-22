import { useMemo } from "react";
import { CardItem, CardPickerModal } from "@/components/picker/CardPickerModal";
import { listWidgets, type WidgetDefinition } from "@/widgets";

type Props = {
  onSelect: (widget: WidgetDefinition) => void;
  onCancel: () => void;
};

export function WidgetPickerModal({ onSelect, onCancel }: Props) {
  const widgets = useMemo(() => listWidgets(), []);
  const byKey = useMemo(() => {
    const m = new Map<string, WidgetDefinition>();
    for (const w of widgets) m.set(w.type, w);
    return m;
  }, [widgets]);
  const items = useMemo<CardItem[]>(
    () =>
      widgets.map((w) => ({
        key: w.type,
        name: w.title,
        description: w.description,
        category: w.category,
        thumbnail: w.thumbnail ?? null
      })),
    [widgets]
  );

  return (
    <CardPickerModal
      items={items}
      selectedKey={null}
      title="Add a widget"
      subtitle="Pick a widget type to add to this dashboard."
      searchPlaceholder="Search widgets"
      confirmLabel="Add widget"
      emptyHint="No widgets registered."
      onSelect={(item) => {
        const original = byKey.get(item.key);
        if (original) onSelect(original);
      }}
      onCancel={onCancel}
    />
  );
}
