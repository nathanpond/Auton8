import { useMemo } from "react";
import { JsxPage } from "@/pages/dynamic-page/JsxPage";

export type JsxFormMode = "edit" | "view";

export type JsxFormHostProps<TData = Record<string, unknown>> = {
  source: string;
  data?: TData;
  onChange?: (next: TData) => void;
  onSubmit?: (payload: TData) => void | Promise<void>;
  mode?: JsxFormMode;
  context?: Record<string, unknown>;
  // Caller-defined extras forwarded under `extras` so consumers don't have
  // to invent new top-level slots for things like `recordType`, `task`,
  // or feature flags.
  extras?: Record<string, unknown>;
};

// JsxFormHost is the single binding surface used wherever a Forms-authored
// JSX form is rendered. Consumers (record forms, workflow tasks, the public
// /form/:shortCode route) forward whatever data they own; the form author's
// `function Page({ data, onChange, onSubmit, mode, context, ...extras })`
// decides what to do with it.
export function JsxFormHost<TData = Record<string, unknown>>({
  source,
  data,
  onChange,
  onSubmit,
  mode = "edit",
  context,
  extras
}: JsxFormHostProps<TData>) {
  const props = useMemo<Record<string, unknown>>(
    () => ({
      data,
      onChange,
      onSubmit,
      mode,
      context: context ?? {},
      ...(extras ?? {})
    }),
    [data, onChange, onSubmit, mode, context, extras]
  );

  return <JsxPage source={source} props={props} />;
}
