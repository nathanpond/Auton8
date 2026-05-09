// Auto-magic form-fill: scans the DOM for <form> elements and exposes
// their fields to the chatbot, plus handlers for set/get/submit. Pages
// don't need to opt in — the registry injects this whenever forms are
// present. Per-form and per-field opt-out via the data-agent-exclude
// attribute (any truthy value works; we just check for the attribute).
//
// Why DOM-based? It's the only mechanism that works regardless of which
// form library a page uses (react-hook-form, plain controlled state,
// Formik, etc.). We use the React-tracker hack to set values so RHF and
// other libraries pick up the change as if a user typed it.

import { PageActionDefinition, PageActionRequest, PageActionResult } from "./types";

export type FormFieldDescriptor = {
  name: string;
  id?: string;
  type: string;
  value: unknown;
  label?: string;
  readOnly?: true;
  disabled?: true;
  required?: true;
  placeholder?: string;
  options?: Array<{ value: string; label: string }>;
};

export type FormDescriptor = {
  formId: string;          // stable selector the model passes back to set_form_field
  name?: string;
  id?: string;
  ariaLabel?: string;
  fields: FormFieldDescriptor[];
};

const EXCLUDE_ATTR = "data-agent-exclude";
// Field types we never expose, regardless of attributes. Submit/reset
// buttons are excluded because the agent doesn't need to know about them
// (it submits via the explicit action). Hidden / password fields are
// security-conservative defaults.
const EXCLUDED_INPUT_TYPES = new Set([
  "password",
  "hidden",
  "submit",
  "reset",
  "button",
  "image",
  "file"
]);

// Walk the live DOM and produce one descriptor per visible form. Returns
// [] when no forms are mounted. Cheap enough to call per snapshot.
export function discoverForms(): FormDescriptor[] {
  if (typeof document === "undefined") return [];
  const forms = Array.from(document.querySelectorAll("form"));
  const out: FormDescriptor[] = [];
  forms.forEach((form, index) => {
    if (form.hasAttribute(EXCLUDE_ATTR)) return;
    const descriptor: FormDescriptor = {
      formId: stableFormId(form, index),
      name: form.getAttribute("name") ?? undefined,
      id: form.getAttribute("id") ?? undefined,
      ariaLabel: form.getAttribute("aria-label") ?? undefined,
      fields: collectFields(form)
    };
    if (descriptor.fields.length === 0) return;
    out.push(descriptor);
  });
  return out;
}

// Builtin actions the registry handles itself. Page providers don't need
// to declare these; they're added to the snapshot whenever forms are
// present.
export const BUILTIN_FORM_ACTIONS: PageActionDefinition[] = [
  {
    name: "set_form_field",
    description:
      "Set the value of a form field on the current page. args: { formId: string, fieldName: string, value: string|number|boolean }. The formId comes from data.forms[].formId in the snapshot. For checkboxes pass true/false; for radios pass the option's value; for selects pass the option's value. The field's React state updates as if the user typed; the user must still click Save."
  },
  {
    name: "get_form_value",
    description:
      "Read the current value of a form field. args: { formId: string, fieldName: string }. Useful when the snapshot may be stale or you want to confirm a value before changing it."
  },
  {
    name: "submit_form",
    description:
      "Submit the form on the current page. args: { formId: string }. Equivalent to clicking the submit button. Use sparingly — most users prefer to review before submitting; ALWAYS get explicit confirmation first."
  }
];

const BUILTIN_ACTION_NAMES = new Set(BUILTIN_FORM_ACTIONS.map((a) => a.name));

export function isBuiltinFormAction(name: string): boolean {
  return BUILTIN_ACTION_NAMES.has(name);
}

// Dispatch a builtin form action against the live DOM. Returns the same
// PageActionResult shape pages return from their onPageAction handler so
// the registry can return either uniformly.
export async function dispatchBuiltinFormAction(req: PageActionRequest): Promise<PageActionResult> {
  switch (req.action) {
    case "set_form_field":
      return setFormField(req.args);
    case "get_form_value":
      return getFormValue(req.args);
    case "submit_form":
      return submitForm(req.args);
    default:
      return { ok: false, error: "unknown_action", message: `Builtin form action '${req.action}' is not supported.` };
  }
}

function setFormField(rawArgs: unknown): PageActionResult {
  const args = rawArgs as { formId?: string; fieldName?: string; value?: unknown } | undefined;
  if (!args?.formId || !args.fieldName) {
    return { ok: false, error: "bad_args", message: "args.formId and args.fieldName are required." };
  }
  const form = findFormById(args.formId);
  if (!form) return { ok: false, error: "not_found", message: `No form '${args.formId}' on the page.` };
  if (form.hasAttribute(EXCLUDE_ATTR)) {
    return { ok: false, error: "excluded", message: "This form is opted out of agent control." };
  }

  const field = findFieldByName(form, args.fieldName);
  if (!field) return { ok: false, error: "not_found", message: `No field '${args.fieldName}' in form '${args.formId}'.` };
  if (field.element.hasAttribute(EXCLUDE_ATTR)) {
    return { ok: false, error: "excluded", message: `Field '${args.fieldName}' is opted out of agent control.` };
  }
  if (field.element.disabled) {
    return { ok: false, error: "disabled", message: `Field '${args.fieldName}' is disabled.` };
  }

  const before = readFieldValue(field.element);
  try {
    writeFieldValue(field.element, args.value);
  } catch (err) {
    return { ok: false, error: "set_failed", message: err instanceof Error ? err.message : String(err) };
  }
  const after = readFieldValue(field.element);

  return {
    ok: true,
    summary: `Set ${describeField(field.element, args.fieldName)} from ${formatValue(before)} to ${formatValue(after)}.`,
    changes: { formId: args.formId, fieldName: args.fieldName, before, after }
  };
}

function getFormValue(rawArgs: unknown): PageActionResult {
  const args = rawArgs as { formId?: string; fieldName?: string } | undefined;
  if (!args?.formId || !args.fieldName) {
    return { ok: false, error: "bad_args", message: "args.formId and args.fieldName are required." };
  }
  const form = findFormById(args.formId);
  if (!form) return { ok: false, error: "not_found", message: `No form '${args.formId}' on the page.` };
  const field = findFieldByName(form, args.fieldName);
  if (!field) return { ok: false, error: "not_found", message: `No field '${args.fieldName}' in form '${args.formId}'.` };
  if (field.element.hasAttribute(EXCLUDE_ATTR)) {
    return { ok: false, error: "excluded", message: `Field '${args.fieldName}' is opted out of agent control.` };
  }
  return {
    ok: true,
    summary: `Read ${describeField(field.element, args.fieldName)}.`,
    changes: { formId: args.formId, fieldName: args.fieldName, value: readFieldValue(field.element) }
  };
}

function submitForm(rawArgs: unknown): PageActionResult {
  const args = rawArgs as { formId?: string } | undefined;
  if (!args?.formId) return { ok: false, error: "bad_args", message: "args.formId is required." };
  const form = findFormById(args.formId);
  if (!form) return { ok: false, error: "not_found", message: `No form '${args.formId}' on the page.` };
  if (form.hasAttribute(EXCLUDE_ATTR)) {
    return { ok: false, error: "excluded", message: "This form is opted out of agent control." };
  }

  // requestSubmit triggers built-in form validation and the submit handler
  // the page registered. Falls back to dispatching submit if not available.
  if (typeof form.requestSubmit === "function") {
    form.requestSubmit();
  } else {
    form.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
  }
  return { ok: true, summary: `Submitted form '${args.formId}'.` };
}

// Lookup helpers --------------------------------------------------------

function findFormById(formId: string): HTMLFormElement | null {
  if (typeof document === "undefined") return null;
  const all = Array.from(document.querySelectorAll<HTMLFormElement>("form"));
  // Match by index-based formId first (stable across one render), then
  // by name / id (looser fallback).
  for (let i = 0; i < all.length; i++) {
    if (stableFormId(all[i], i) === formId) return all[i];
  }
  for (const form of all) {
    if (form.getAttribute("name") === formId) return form;
    if (form.getAttribute("id") === formId) return form;
  }
  return null;
}

function findFieldByName(form: HTMLFormElement, fieldName: string): { element: FormFieldElement } | null {
  // Prefer name attribute (the canonical case for react-hook-form), then
  // id, then a labelled input. Element collection lookup hits all three
  // for free for name/id but we keep it explicit for clarity.
  const byName = form.querySelector<FormFieldElement>(
    `[name="${cssEscape(fieldName)}"]`
  );
  if (byName && isFormFieldElement(byName)) return { element: byName };

  const byId = form.querySelector<FormFieldElement>(`#${cssEscape(fieldName)}`);
  if (byId && isFormFieldElement(byId)) return { element: byId };

  const labelled = Array.from(form.querySelectorAll("label")).find(
    (l) => l.textContent?.trim() === fieldName
  );
  if (labelled) {
    const target = labelled.htmlFor
      ? form.querySelector<FormFieldElement>(`#${cssEscape(labelled.htmlFor)}`)
      : labelled.querySelector<FormFieldElement>("input, select, textarea");
    if (target && isFormFieldElement(target)) return { element: target };
  }
  return null;
}

type FormFieldElement = HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement;

function isFormFieldElement(el: Element): el is FormFieldElement {
  return el instanceof HTMLInputElement || el instanceof HTMLSelectElement || el instanceof HTMLTextAreaElement;
}

function collectFields(form: HTMLFormElement): FormFieldDescriptor[] {
  const elements = Array.from(form.querySelectorAll<FormFieldElement>("input, select, textarea"));
  const out: FormFieldDescriptor[] = [];
  for (const el of elements) {
    if (el.hasAttribute(EXCLUDE_ATTR)) continue;
    const type = (el instanceof HTMLInputElement ? el.type : el.tagName.toLowerCase()).toLowerCase();
    if (EXCLUDED_INPUT_TYPES.has(type)) continue;
    if (!el.name && !el.id) continue;

    const descriptor: FormFieldDescriptor = {
      name: el.name || el.id,
      id: el.id || undefined,
      type,
      value: readFieldValue(el),
      label: findLabelText(form, el),
      placeholder: el instanceof HTMLInputElement || el instanceof HTMLTextAreaElement
        ? el.placeholder || undefined
        : undefined
    };
    const isReadOnly = el instanceof HTMLSelectElement
      ? el.hasAttribute("readonly")
      : el.readOnly === true;
    if (isReadOnly) descriptor.readOnly = true;
    if (el.disabled) descriptor.disabled = true;
    if (el.required) descriptor.required = true;

    if (el instanceof HTMLSelectElement) {
      descriptor.options = Array.from(el.options).map((o) => ({
        value: o.value,
        label: o.label || o.textContent?.trim() || o.value
      }));
    }
    out.push(descriptor);
  }
  return out;
}

function findLabelText(form: HTMLFormElement, el: FormFieldElement): string | undefined {
  if (el.id) {
    const label = form.querySelector<HTMLLabelElement>(`label[for="${cssEscape(el.id)}"]`);
    if (label?.textContent?.trim()) return label.textContent.trim();
  }
  const ariaLabel = el.getAttribute("aria-label");
  if (ariaLabel) return ariaLabel;
  // Walk up: <label><input /></label> wraps without `for`.
  const wrapping = el.closest("label");
  if (wrapping?.textContent?.trim()) return wrapping.textContent.trim();
  return undefined;
}

function readFieldValue(el: FormFieldElement): unknown {
  if (el instanceof HTMLInputElement) {
    if (el.type === "checkbox") return el.checked;
    if (el.type === "radio") return el.checked ? el.value : undefined;
    if (el.type === "number") {
      const n = Number(el.value);
      return Number.isFinite(n) ? n : el.value;
    }
    return el.value;
  }
  if (el instanceof HTMLSelectElement) {
    if (el.multiple) return Array.from(el.selectedOptions).map((o) => o.value);
    return el.value;
  }
  return el.value;
}

// Cross-library value setter. React installs its own value setter on the
// input prototype that tracks the previous value; bypassing it via the
// native setter and dispatching an input event is the documented pattern
// for getting React/RHF to pick up a programmatic change.
function writeFieldValue(el: FormFieldElement, value: unknown): void {
  if (el instanceof HTMLInputElement && el.type === "checkbox") {
    setNativeChecked(el, Boolean(value));
    el.dispatchEvent(new Event("input", { bubbles: true }));
    el.dispatchEvent(new Event("change", { bubbles: true }));
    return;
  }
  if (el instanceof HTMLInputElement && el.type === "radio") {
    // Radios share a name within a form. Find the one whose value matches.
    const form = el.form;
    if (!form) throw new Error("Radio input is detached from a form.");
    const target = form.querySelector<HTMLInputElement>(
      `input[type="radio"][name="${cssEscape(el.name)}"][value="${cssEscape(String(value))}"]`
    );
    if (!target) throw new Error(`No radio with value '${String(value)}' for '${el.name}'.`);
    setNativeChecked(target, true);
    target.dispatchEvent(new Event("input", { bubbles: true }));
    target.dispatchEvent(new Event("change", { bubbles: true }));
    return;
  }

  const stringValue = value == null ? "" : String(value);
  setNativeValue(el, stringValue);
  el.dispatchEvent(new Event("input", { bubbles: true }));
  el.dispatchEvent(new Event("change", { bubbles: true }));
}

function setNativeValue(el: FormFieldElement, value: string): void {
  // Get the native (non-React) setter from the prototype chain.
  const proto = Object.getPrototypeOf(el);
  const protoSetter = Object.getOwnPropertyDescriptor(proto, "value")?.set;
  const ownSetter = Object.getOwnPropertyDescriptor(el, "value")?.set;
  if (protoSetter && protoSetter !== ownSetter) {
    protoSetter.call(el, value);
  } else if (ownSetter) {
    ownSetter.call(el, value);
  } else {
    (el as { value: string }).value = value;
  }
}

function setNativeChecked(el: HTMLInputElement, value: boolean): void {
  const proto = Object.getPrototypeOf(el);
  const protoSetter = Object.getOwnPropertyDescriptor(proto, "checked")?.set;
  const ownSetter = Object.getOwnPropertyDescriptor(el, "checked")?.set;
  if (protoSetter && protoSetter !== ownSetter) {
    protoSetter.call(el, value);
  } else if (ownSetter) {
    ownSetter.call(el, value);
  } else {
    el.checked = value;
  }
}

function describeField(el: FormFieldElement, fieldName: string): string {
  return el instanceof HTMLInputElement ? `${el.type || "field"} '${fieldName}'` : `${el.tagName.toLowerCase()} '${fieldName}'`;
}

function formatValue(value: unknown): string {
  if (value == null) return "(empty)";
  if (typeof value === "boolean") return value ? "true" : "false";
  if (typeof value === "string") return value.length > 80 ? `"${value.slice(0, 79)}…"` : `"${value}"`;
  return String(value);
}

function stableFormId(form: HTMLFormElement, index: number): string {
  return `forms[${index}]`;
}

// CSS.escape isn't in older browsers; fall back to a tiny escape that
// covers the characters likely to appear in form-field names.
function cssEscape(value: string): string {
  if (typeof CSS !== "undefined" && typeof CSS.escape === "function") return CSS.escape(value);
  return value.replace(/(["'\\\[\]:.\s])/g, "\\$1");
}
