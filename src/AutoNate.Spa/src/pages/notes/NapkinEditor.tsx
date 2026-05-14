import { useState } from "react";
import { NoteDto } from "@/api/content";
import { notesTheme } from "./notesTheme";

type Props = {
  note: NoteDto | null;
  noteName: string;
};

// Napkin = Excalidraw-style sketch surface. The full Excalidraw integration is
// a follow-up — this component renders the design's toolbar chrome plus a
// dotted-paper placeholder so layout and styling can be validated end-to-end
// without pulling in the @excalidraw/excalidraw bundle yet.
export function NapkinEditor({ note, noteName }: Props) {
  const [activeTool, setActiveTool] = useState<string>("select");

  return (
    <div
      style={{
        flex: 1,
        display: "flex",
        flexDirection: "column",
        minHeight: 0,
        background: "#fff"
      }}
    >
      <NapkinToolbar activeTool={activeTool} setActiveTool={setActiveTool} />

      <div style={{ flex: 1, position: "relative", overflow: "hidden", background: "#fafaf7" }}>
        <div
          style={{
            position: "absolute",
            inset: 0,
            backgroundImage: "radial-gradient(circle, #d8d4cc 1px, transparent 1px)",
            backgroundSize: "20px 20px",
            opacity: 0.6
          }}
        />

        <div
          style={{
            position: "relative",
            zIndex: 1,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            height: "100%",
            color: notesTheme.muted,
            padding: 24,
            textAlign: "center"
          }}
        >
          <div style={{ maxWidth: 360 }}>
            <i
              className="fa fa-pen-nib"
              style={{
                fontSize: 36,
                color: "#f59c1a",
                display: "block",
                marginBottom: 12
              }}
            />
            <div
              style={{
                fontSize: 14,
                fontWeight: 700,
                color: notesTheme.dark,
                marginBottom: 4
              }}
            >
              {note?.title ?? noteName}
            </div>
            <div style={{ fontSize: 12 }}>
              Excalidraw canvas will mount here. Drawing JSON is persisted to{" "}
              <code>notes.content_jsonb</code> via the same auto-save path used by Visual Text.
            </div>
          </div>
        </div>

        <FloatPanel pos={{ bottom: 16, left: 16 }}>
          <i className="fa fa-magnifying-glass-minus" style={ftIcon} />
          <span style={{ fontSize: 11, fontWeight: 700, color: notesTheme.dark, width: 36, textAlign: "center" }}>
            100%
          </span>
          <i className="fa fa-magnifying-glass-plus" style={ftIcon} />
          <div style={{ width: 1, height: 16, background: notesTheme.border, margin: "0 4px" }} />
          <i className="fa fa-hand" style={ftIcon} />
          <i className="fa fa-crosshairs" style={ftIcon} />
        </FloatPanel>

        <FloatPanel pos={{ bottom: 16, right: 16 }}>
          <i className="fa fa-users" style={{ ...ftIcon, color: notesTheme.accent }} />
          <span style={{ fontSize: 11, fontWeight: 700, color: notesTheme.dark }}>
            You only
          </span>
        </FloatPanel>
      </div>
    </div>
  );
}

function NapkinToolbar({
  activeTool,
  setActiveTool
}: {
  activeTool: string;
  setActiveTool: (id: string) => void;
}) {
  const tools = [
    { id: "select", icon: "fa-arrow-pointer", label: "Select" },
    { id: "pen", icon: "fa-pen-nib", label: "Pen" },
    { id: "rect", icon: "fa-square", label: "Rectangle" },
    { id: "circle", icon: "fa-circle", label: "Ellipse" },
    { id: "arrow", icon: "fa-arrow-right", label: "Arrow" },
    { id: "text", icon: "fa-font", label: "Text" },
    { id: "sticky", icon: "fa-note-sticky", label: "Sticky" },
    { id: "image", icon: "fa-image", label: "Image" },
    { id: "eraser", icon: "fa-eraser", label: "Eraser" }
  ];
  return (
    <div
      style={{
        display: "flex",
        alignItems: "center",
        gap: 6,
        padding: "8px 12px",
        borderBottom: `1px solid ${notesTheme.border}`,
        background: "#fff",
        minHeight: 44,
        flexShrink: 0
      }}
    >
      <div
        style={{
          display: "inline-flex",
          gap: 2,
          background: notesTheme.hover,
          borderRadius: 6,
          padding: 3
        }}
      >
        {tools.map((t) => (
          <button
            key={t.id}
            type="button"
            onClick={() => setActiveTool(t.id)}
            title={t.label}
            style={{
              width: 30,
              height: 28,
              border: "none",
              background: activeTool === t.id ? "#fff" : "transparent",
              boxShadow: activeTool === t.id ? "0 1px 2px rgba(0,0,0,0.08)" : "none",
              borderRadius: 4,
              color: activeTool === t.id ? notesTheme.primary : notesTheme.dark,
              cursor: "pointer",
              fontSize: 12
            }}
          >
            <i className={`fa ${t.icon}`} />
          </button>
        ))}
      </div>

      <div style={{ width: 1, height: 22, background: notesTheme.border, margin: "0 4px" }} />

      <span style={{ fontSize: 11, color: notesTheme.muted, fontWeight: 600 }}>Stroke</span>
      {["#1e2a3a", "#e8590c", "#1971c2", "#2f9e44"].map((c, i) => (
        <ColorSwatch key={c} color={c} active={i === 0} />
      ))}

      <span style={{ fontSize: 11, color: notesTheme.muted, fontWeight: 600, marginLeft: 8 }}>
        Fill
      </span>
      <ColorSwatch color="#fff" border />
      {["#fff3bf", "#d3f9d8", "#ffe3e3"].map((c) => (
        <ColorSwatch key={c} color={c} />
      ))}

      <div style={{ marginLeft: "auto", display: "flex", alignItems: "center", gap: 6 }}>
        <span style={{ fontSize: 11, color: notesTheme.muted }}>Hand-drawn</span>
        <Toggle on />
      </div>
    </div>
  );
}

function ColorSwatch({
  color,
  active,
  border
}: {
  color: string;
  active?: boolean;
  border?: boolean;
}) {
  return (
    <button
      type="button"
      style={{
        width: 20,
        height: 20,
        border: border ? `1px solid ${notesTheme.border}` : "none",
        background: color,
        borderRadius: 4,
        cursor: "pointer",
        padding: 0,
        boxShadow: active ? `0 0 0 2px ${notesTheme.primary}` : "none"
      }}
    />
  );
}

function Toggle({ on: initialOn }: { on?: boolean }) {
  const [on, setOn] = useState(!!initialOn);
  return (
    <button
      type="button"
      onClick={() => setOn(!on)}
      style={{
        width: 30,
        height: 18,
        borderRadius: 99,
        border: "none",
        background: on ? notesTheme.primary : notesTheme.border,
        position: "relative",
        cursor: "pointer",
        padding: 0
      }}
    >
      <span
        style={{
          position: "absolute",
          top: 2,
          left: on ? 14 : 2,
          width: 14,
          height: 14,
          borderRadius: "50%",
          background: "#fff",
          transition: "left 120ms"
        }}
      />
    </button>
  );
}

function FloatPanel({
  children,
  pos
}: {
  children: React.ReactNode;
  pos: { top?: number; left?: number; right?: number; bottom?: number };
}) {
  return (
    <div
      style={{
        position: "absolute",
        ...pos,
        display: "inline-flex",
        alignItems: "center",
        gap: 6,
        background: "#fff",
        border: `1px solid ${notesTheme.border}`,
        borderRadius: 8,
        padding: "6px 10px",
        boxShadow: "0 4px 14px rgba(0,0,0,0.08)"
      }}
    >
      {children}
    </div>
  );
}

const ftIcon: React.CSSProperties = {
  fontSize: 12,
  color: notesTheme.dark,
  cursor: "pointer",
  padding: 4
};
