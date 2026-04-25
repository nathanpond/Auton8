import { FieldDataType } from "@/types/records";

// Default config payload for each built-in data type. Matches the server's
// NormalizeConfig output so the first round-trip is a no-op. Unknown types
// default to `{}` which the server will reject, prompting the user to pick a
// known type.
export function defaultFieldConfig(dataType: FieldDataType): Record<string, unknown> {
  switch (dataType) {
    case "text":
      return { variant: "single", maxLength: 4000 };
    case "number":
      return { variant: "decimal", precision: 2, min: null, max: null };
    case "date":
      return { variant: "date" };
    case "phone":
      return { region: "US" };
    case "email":
      return {};
    case "option":
      return { multi: false, choices: [{ value: "option_a", label: "Option A" }] };
    case "boolean":
      return {};
    default:
      return {};
  }
}

export function humanDataType(dataType: FieldDataType): string {
  switch (dataType) {
    case "text":
      return "Text";
    case "number":
      return "Number";
    case "date":
      return "Date";
    case "phone":
      return "Phone";
    case "email":
      return "Email";
    case "option":
      return "Option";
    case "boolean":
      return "True/False";
    default:
      return dataType;
  }
}
