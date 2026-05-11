import { ColorInput } from "@mantine/core";

interface ColorPickerProps {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  id?: string;
}

export default function ColorPicker({ value, onChange, placeholder, id }: ColorPickerProps) {
  return (
    <ColorInput
      id={id}
      value={value}
      onChange={onChange}
      placeholder={placeholder ?? "#336699"}
      format="hex"
      withEyeDropper
      popoverProps={{ withinPortal: true, position: "bottom-start" }}
    />
  );
}
