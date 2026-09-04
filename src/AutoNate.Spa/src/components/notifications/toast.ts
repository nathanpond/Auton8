import { notifications } from "@mantine/notifications";

/**
 * The one way the SPA raises a transient notification.
 *
 * ## Toast, or in-page Alert?
 *
 * **Toast** — something transient that the user just caused. "Saved.",
 * "Failed to grant access.", "Copied to clipboard." It is feedback on an
 * action, it is not part of the page, and the page still makes sense once it
 * has gone.
 *
 * **In-page `<Alert>`** — a condition that belongs to the page: validation
 * summaries, empty states, "this record is archived", "no provider is
 * configured". It is still true after a reload, and it should still be on
 * screen when the user comes back to look.
 *
 * The rule is written up in CLAUDE.md under "Notifications"; this comment is
 * the copy the next person actually finds, because they will be here rather
 * than there.
 *
 * ## Why a module and not each call site configuring Mantine
 *
 * Accessibility is decided here, once, rather than at 91 call sites:
 *
 * - An **error** is announced assertively (`role="alert"`) and **never
 *   auto-dismisses**. An error announced politely can be missed entirely, and
 *   one that disappears before it is read is worse than no error at all.
 * - **Success** and **info** are announced politely (`role="status"`) and time
 *   out, because interrupting someone to say a thing worked is rude.
 * - Everything keeps its close button, so every toast is dismissible from the
 *   keyboard.
 * - Focus is never moved. The live region is what carries the message; stealing
 *   focus would take the user out of whatever they were doing.
 *
 * Importing `@mantine/notifications` anywhere else is an ESLint error — see the
 * `no-restricted-imports` rule in eslint.config.js. The wrapper is only worth
 * having if it cannot be routed around by habit.
 */

/** Long enough to read a sentence without being in the way. */
const SUCCESS_TIMEOUT_MS = 4000;

/**
 * Longer than success: a warning is usually something the user needs to act on
 * later, so it earns more reading time without earning permanence.
 */
const WARNING_TIMEOUT_MS = 10000;

const INFO_TIMEOUT_MS = 6000;

type ToastOptions = {
  /** Optional bold line above the message. */
  title?: string;
  /** Overrides the default id, so a repeated toast replaces rather than stacks. */
  id?: string;
};

function show(
  message: string,
  color: string,
  role: "alert" | "status",
  autoClose: number | false,
  options?: ToastOptions
) {
  notifications.show({
    id: options?.id,
    title: options?.title,
    message,
    color,
    autoClose,
    withCloseButton: true,
    // Mantine renders this on the notification element. `alert` is an implicit
    // aria-live="assertive" region and `status` an implicit polite one, which
    // is why the severity split is expressed as a role rather than by setting
    // aria-live by hand.
    role,
  });
}

export const toast = {
  /**
   * Something the user did worked. Polite, auto-dismissed.
   */
  success(message: string, options?: ToastOptions) {
    show(message, "green", "status", SUCCESS_TIMEOUT_MS, options);
  },

  /**
   * Something the user did failed.
   *
   * Assertive and persistent, deliberately. This is the one severity where the
   * defaults have to fight the framework's: Mantine auto-dismisses everything
   * by default, and an error the user never read is indistinguishable from an
   * action that silently did nothing.
   */
  error(message: string, options?: ToastOptions) {
    show(message, "red", "alert", false, options);
  },

  /** Something worked but deserves attention. Polite, generous timeout. */
  warning(message: string, options?: ToastOptions) {
    show(message, "yellow", "status", WARNING_TIMEOUT_MS, options);
  },

  /** Neutral feedback. Polite, auto-dismissed. */
  info(message: string, options?: ToastOptions) {
    show(message, "blue", "status", INFO_TIMEOUT_MS, options);
  },

  /** Dismisses a toast raised with an explicit id. */
  hide(id: string) {
    notifications.hide(id);
  },
};

/** Exported for tests only. */
export const TOAST_TIMEOUTS = {
  success: SUCCESS_TIMEOUT_MS,
  warning: WARNING_TIMEOUT_MS,
  info: INFO_TIMEOUT_MS,
  error: false as const,
};
