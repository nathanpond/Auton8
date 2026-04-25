import { useId } from "react";

interface ColorPickerProps {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  id?: string;
}

const HEX_RE = /^#([0-9a-f]{3}|[0-9a-f]{6})$/i;

function normalizeHex(value: string): string | null {
  if (!HEX_RE.test(value)) return null;
  if (value.length === 4) {
    const r = value[1];
    const g = value[2];
    const b = value[3];
    return `#${r}${r}${g}${g}${b}${b}`.toLowerCase();
  }
  return value.toLowerCase();
}

export default function ColorPicker({ value, onChange, placeholder, id }: ColorPickerProps) {
  const autoId = useId();
  const inputId = id ?? autoId;

  const swatchValue = normalizeHex(value.trim()) ?? "#cccccc";
  const isValid = value.trim() === "" || normalizeHex(value.trim()) !== null;

  return (
    <div className="input-group">
      <label
        htmlFor={`${inputId}-swatch`}
        className="input-group-text p-0 overflow-hidden"
        style={{ width: "2.75rem", cursor: "pointer" }}
        title="Pick a color"
      >
        <input
          id={`${inputId}-swatch`}
          type="color"
          value={swatchValue}
          onChange={(e) => onChange(e.target.value)}
          style={{
            width: "100%",
            height: "100%",
            border: 0,
            padding: 0,
            background: "transparent",
            cursor: "pointer"
          }}
          aria-label="Color swatch"
        />
      </label>
      <input
        id={inputId}
        className={`form-control font-monospace ${isValid ? "" : "is-invalid"}`}
        autoComplete="off"
        spellCheck={false}
        placeholder={placeholder ?? "#336699"}
        value={value}
        onChange={(e) => onChange(e.target.value)}
      />
    </div>
  );
}
