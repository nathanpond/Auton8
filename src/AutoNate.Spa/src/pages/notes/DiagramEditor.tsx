import { NoteDto } from "@/api/content";
import { notesTheme } from "./notesTheme";

type Props = {
  note: NoteDto | null;
  noteName: string;
};

// Diagram = Draw.io / diagrams.net. Like Napkin, this is the visual chrome
// (top toolbar, left shape palette, right format panel) with a grid-paper
// placeholder canvas. The real Draw.io integration plugs into the canvas
// region via the embed iframe in a follow-up; the toolbar layout here is
// already what the design demands.
export function DiagramEditor({ note, noteName }: Props) {
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
      <DiagramToolbar />
      <div style={{ flex: 1, display: "flex", minHeight: 0 }}>
        <ShapePalette />
        <DiagramCanvas title={note?.title ?? noteName} />
        <FormatPanel />
      </div>
    </div>
  );
}

function DiagramToolbar() {
  const Div = () => (
    <div style={{ width: 1, height: 18, background: notesTheme.border, margin: "0 4px" }} />
  );
  return (
    <div
      style={{
        display: "flex",
        alignItems: "center",
        gap: 1,
        padding: "5px 10px",
        borderBottom: `1px solid ${notesTheme.border}`,
        background: notesTheme.hover,
        minHeight: 36,
        flexShrink: 0
      }}
    >
      <Btn icon="fa-clock-rotate-left" />
      <Btn icon="fa-rotate-right" />
      <Div />
      <Btn icon="fa-cut" />
      <Btn icon="fa-copy" />
      <Btn icon="fa-paste" />
      <Div />
      <Btn icon="fa-magnifying-glass-plus" />
      <Btn icon="fa-magnifying-glass-minus" />
      <span style={{ fontSize: 11, color: notesTheme.muted, margin: "0 6px", minWidth: 34 }}>
        100%
      </span>
      <Btn icon="fa-expand" />
      <Div />
      <Btn icon="fa-square" active />
      <Btn icon="fa-circle" />
      <Btn icon="fa-diamond" />
      <Btn icon="fa-arrow-right" />
      <Btn icon="fa-font" />
      <Div />
      <Btn icon="fa-align-left" />
      <Btn icon="fa-align-center" />
      <Btn icon="fa-align-right" />
      <Div />
      <Btn icon="fa-bold" />
      <Btn icon="fa-italic" />
      <Btn icon="fa-underline" />
      <Div />
      <Btn icon="fa-layer-group" />
      <Btn icon="fa-clone" />
      <Btn icon="fa-trash" />

      <div style={{ marginLeft: "auto", display: "inline-flex", alignItems: "center", gap: 6 }}>
        <span style={{ fontSize: 11, color: notesTheme.muted }}>Page 1 of 1</span>
        <Btn icon="fa-plus" />
      </div>
    </div>
  );
}

function Btn({ icon, active }: { icon: string; active?: boolean }) {
  return (
    <button
      type="button"
      style={{
        width: 28,
        height: 26,
        border: "none",
        background: active ? notesTheme.selected : "transparent",
        borderRadius: 3,
        color: active ? notesTheme.primary : notesTheme.dark,
        cursor: "pointer",
        fontSize: 11
      }}
    >
      <i className={`fa ${icon}`} />
    </button>
  );
}

function ShapePalette() {
  const flowchart = [
    { label: "Process" },
    { label: "Terminal" },
    { label: "Decision" },
    { label: "Data" },
    { label: "Document" },
    { label: "Conn." },
    { label: "Database" },
    { label: "Note" }
  ];
  return (
    <aside
      style={{
        width: 180,
        flexShrink: 0,
        borderRight: `1px solid ${notesTheme.border}`,
        background: "#fff",
        overflowY: "auto",
        padding: "10px 8px"
      }}
    >
      <SectionTitle>Flowchart</SectionTitle>
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "1fr 1fr",
          gap: 6,
          marginBottom: 14
        }}
      >
        {flowchart.map((s) => (
          <ShapeTile key={s.label} label={s.label} />
        ))}
      </div>

      <SectionTitle>UML</SectionTitle>
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "1fr 1fr",
          gap: 6,
          marginBottom: 14
        }}
      >
        {["Class", "Actor", "Use case", "Package"].map((label) => (
          <ShapeTile key={label} label={label} />
        ))}
      </div>

      <SectionTitle>AWS</SectionTitle>
      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 6 }}>
        {["VPC", "EC2", "RDS", "S3"].map((label) => (
          <ShapeTile key={label} label={label} />
        ))}
      </div>
    </aside>
  );
}

function SectionTitle({ children }: { children: React.ReactNode }) {
  return (
    <div
      style={{
        fontSize: 10.5,
        fontWeight: 700,
        color: notesTheme.muted,
        textTransform: "uppercase",
        letterSpacing: "0.06em",
        margin: "6px 4px"
      }}
    >
      {children}
    </div>
  );
}

function ShapeTile({ label }: { label: string }) {
  return (
    <button
      type="button"
      style={{
        background: "#fafbfc",
        border: `1px solid ${notesTheme.border}`,
        borderRadius: 4,
        padding: "10px 4px",
        cursor: "grab",
        fontSize: 10.5,
        fontWeight: 600,
        color: notesTheme.dark,
        fontFamily: "inherit",
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        gap: 6
      }}
    >
      <div
        style={{
          width: 28,
          height: 22,
          background: "#fff",
          border: `1px solid ${notesTheme.border}`,
          borderRadius: 3
        }}
      />
      <span>{label}</span>
    </button>
  );
}

function DiagramCanvas({ title }: { title: string }) {
  return (
    <div
      style={{
        flex: 1,
        position: "relative",
        overflow: "hidden",
        background:
          "repeating-linear-gradient(0deg, transparent 0 19px, #eef0f2 19px 20px), " +
          "repeating-linear-gradient(90deg, transparent 0 19px, #eef0f2 19px 20px), #fff"
      }}
    >
      <div
        style={{
          position: "absolute",
          inset: 0,
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          color: notesTheme.muted,
          textAlign: "center"
        }}
      >
        <div style={{ maxWidth: 360 }}>
          <i
            className="fa fa-diagram-project"
            style={{
              fontSize: 36,
              color: "#7950f2",
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
            {title}
          </div>
          <div style={{ fontSize: 12 }}>
            Draw.io canvas will mount here. XML/JSON is persisted to{" "}
            <code>notes.content_jsonb</code>.
          </div>
        </div>
      </div>
    </div>
  );
}

function FormatPanel() {
  return (
    <aside
      style={{
        width: 240,
        flexShrink: 0,
        borderLeft: `1px solid ${notesTheme.border}`,
        background: "#fff",
        padding: "12px 14px",
        overflowY: "auto"
      }}
    >
      <SectionTitle>Style</SectionTitle>
      <div style={{ display: "flex", gap: 6, marginBottom: 12 }}>
        {["Fill", "Border", "Shadow"].map((label) => (
          <div
            key={label}
            style={{
              flex: 1,
              padding: "6px 8px",
              border: `1px solid ${notesTheme.border}`,
              borderRadius: 4,
              fontSize: 11,
              textAlign: "center",
              color: notesTheme.dark,
              fontWeight: 600
            }}
          >
            {label}
          </div>
        ))}
      </div>

      <SectionTitle>Arrange</SectionTitle>
      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 6, marginBottom: 12 }}>
        {["Width", "Height", "X", "Y"].map((label) => (
          <label
            key={label}
            style={{ fontSize: 10.5, color: notesTheme.muted, fontWeight: 600 }}
          >
            {label}
            <input
              defaultValue=""
              placeholder="—"
              style={{
                width: "100%",
                marginTop: 2,
                padding: "5px 6px",
                fontSize: 11,
                border: `1px solid ${notesTheme.border}`,
                borderRadius: 3,
                background: "#fff",
                outline: "none",
                fontFamily: "inherit"
              }}
            />
          </label>
        ))}
      </div>

      <SectionTitle>Line</SectionTitle>
      <div style={{ display: "flex", gap: 6 }}>
        {["Solid", "Dashed", "Dotted"].map((l) => (
          <button
            key={l}
            type="button"
            style={{
              flex: 1,
              padding: "5px 8px",
              border: `1px solid ${notesTheme.border}`,
              borderRadius: 3,
              fontSize: 11,
              background: "#fff",
              color: notesTheme.dark,
              cursor: "pointer",
              fontFamily: "inherit"
            }}
          >
            {l}
          </button>
        ))}
      </div>
    </aside>
  );
}
