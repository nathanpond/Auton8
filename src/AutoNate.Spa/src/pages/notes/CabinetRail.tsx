import { useState } from "react";
import { Tooltip } from "@mantine/core";
import { CabinetDto } from "@/api/content";
import { cabinetColorFor, defaultCabinetIcon, notesTheme } from "./notesTheme";

type Props = {
  cabinets: CabinetDto[];
  activeId: string | null;
  onPick: (id: string) => void;
  onNew?: () => void;
  canCreate?: boolean;
  onOpenSettings?: () => void;
  canOpenSettings?: boolean;
};

export function CabinetRail({
  cabinets,
  activeId,
  onPick,
  onNew,
  canCreate = true,
  onOpenSettings,
  canOpenSettings = true
}: Props) {
  return (
    <nav
      style={{
        width: 64,
        flexShrink: 0,
        borderRight: `1px solid ${notesTheme.border}`,
        background: "#fff",
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        padding: "10px 0",
        gap: 6
      }}
    >
      <div
        style={{
          flex: 1,
          minHeight: 0,
          overflowY: "auto",
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
          gap: 6,
          width: "100%"
        }}
      >
        {cabinets.map((c) => (
          <CabinetTile
            key={c.id}
            cabinet={c}
            active={c.id === activeId}
            onClick={() => onPick(c.id)}
          />
        ))}

        <button
          type="button"
          title={canCreate ? "New cabinet" : "Select a project to add cabinets"}
          onClick={onNew}
          disabled={!canCreate || !onNew}
          style={{
            width: 40,
            height: 40,
            borderRadius: 6,
            marginTop: 4,
            background: "#fff",
            color: notesTheme.muted,
            border: `1px dashed ${notesTheme.border}`,
            cursor: canCreate && onNew ? "pointer" : "not-allowed",
            fontSize: 12,
            opacity: canCreate && onNew ? 1 : 0.5
          }}
        >
          <i className="fa fa-plus" />
        </button>
      </div>

      <div
        style={{
          width: "100%",
          borderTop: `1px solid ${notesTheme.border}`,
          padding: "8px 0 0",
          display: "flex",
          justifyContent: "center"
        }}
      >
        <Tooltip label="Project Settings" position="right" withArrow>
          <button
            type="button"
            onClick={onOpenSettings}
            disabled={!canOpenSettings || !onOpenSettings}
            aria-label="Project Settings"
            style={{
              width: 40,
              height: 40,
              borderRadius: 6,
              background: "#fff",
              color: notesTheme.muted,
              border: `1px solid ${notesTheme.border}`,
              cursor: canOpenSettings && onOpenSettings ? "pointer" : "not-allowed",
              fontSize: 14,
              opacity: canOpenSettings && onOpenSettings ? 1 : 0.5,
              display: "inline-flex",
              alignItems: "center",
              justifyContent: "center"
            }}
          >
            <i className="fa fa-gear" />
          </button>
        </Tooltip>
      </div>
    </nav>
  );
}

function CabinetTile({
  cabinet,
  active,
  onClick
}: {
  cabinet: CabinetDto;
  active: boolean;
  onClick: () => void;
}) {
  const [hover, setHover] = useState(false);
  const color = cabinetColorFor(cabinet.id);
  const icon = cabinet.icon ?? defaultCabinetIcon();
  return (
    <button
      type="button"
      onClick={onClick}
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => setHover(false)}
      title={cabinet.name}
      style={{
        position: "relative",
        width: 40,
        height: 40,
        borderRadius: 6,
        background: active ? color : hover ? color + "15" : "#fff",
        color: active ? "#fff" : color,
        border: active ? "none" : `1px solid ${notesTheme.border}`,
        cursor: "pointer",
        fontSize: 14,
        transition: "all 100ms"
      }}
    >
      <i className={`fa ${icon}`} />
      {active && (
        <span
          style={{
            position: "absolute",
            left: -10,
            top: 6,
            bottom: 6,
            width: 3,
            borderRadius: 2,
            background: color
          }}
        />
      )}
    </button>
  );
}
