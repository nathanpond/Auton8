// Print-to-PDF for the currently viewed page or note. Uses the browser's
// built-in print dialog so the user can "Save as PDF" (every modern browser
// exposes that destination). Captures the rendered editor surface verbatim,
// which keeps formatting consistent with what the user sees on screen.
//
// We don't ship a PDF library here because BlockNote's rendered DOM is
// already styled — driving the browser's print pipeline gives the user the
// same fidelity without bundling a several-hundred-KB renderer.

type ExportArgs = {
  // Whether the user is on the page tab (export the whole page, including
  // PageOverview content) or a note tab (export just that note).
  onPageTab: boolean;
  pageTitle: string;
  noteTitle: string;
};

// CSS classes that the SPA's editor surfaces apply to their root container.
// We look for these in priority order; first match wins.
const PAGE_ROOT_SELECTORS = [
  ".notes-editor-bleed", // PageOverview's outer container
  "main > div:last-child" // generic fallback
];

const NOTE_ROOT_SELECTORS = [
  ".bn-container", // BlockNote rendered editor (rich text notes)
  ".excalidraw", // drawing notes
  "main .bn-editor",
  "main > div:last-child"
];

export function exportToPdf({ onPageTab, pageTitle, noteTitle }: ExportArgs): void {
  const selectors = onPageTab ? PAGE_ROOT_SELECTORS : NOTE_ROOT_SELECTORS;
  let captureEl: Element | null = null;
  for (const sel of selectors) {
    const el = document.querySelector(sel);
    if (el) {
      captureEl = el;
      break;
    }
  }
  if (!captureEl) {
    // Nothing to print — surface a console warning so a developer can find
    // the missing selector in the editor tree. User experience: the menu
    // item silently no-ops; not great, but better than an alert.
    console.warn("[exportToPdf] No printable content found on the current view.");
    return;
  }

  const cloned = captureEl.cloneNode(true) as HTMLElement;

  // Pull every <style> + <link rel=stylesheet> from the host document into
  // the popup so the print preview keeps Mantine + BlockNote + FontAwesome
  // styling intact. Using a popup window instead of a hidden iframe means
  // the browser's print dialog runs fully isolated; nothing in the host
  // app's reactivity can race the printout.
  const styleHtml = Array.from(document.head.querySelectorAll("link[rel='stylesheet'], style"))
    .map((node) => node.outerHTML)
    .join("\n");

  const title = onPageTab ? pageTitle : noteTitle;
  const printDoc = `<!doctype html>
<html>
<head>
<meta charset="utf-8">
<title>${escapeHtml(title)}</title>
${styleHtml}
<style>
  /* Reset chrome that doesn't make sense in a print export. */
  body { margin: 0; padding: 24px; background: #fff; color: #000; font-family: 'Open Sans', system-ui, sans-serif; }
  /* Hide BlockNote slash menus / floating toolbars / drag handles — these
     are interactive surfaces that have no meaning in a static print. */
  .bn-side-menu,
  .bn-formatting-toolbar,
  .bn-suggestion-menu,
  .bn-link-toolbar,
  .bn-image-toolbar,
  .mantine-Tooltip-tooltip,
  .mantine-Popover-dropdown {
    display: none !important;
  }
  /* Force the cloned editor surface to fill the page width and grow
     naturally; the source DOM uses flex layout for the editor pane. */
  .print-root { width: 100%; max-width: 760px; margin: 0 auto; }
  .print-title { font-size: 28px; font-weight: 700; margin: 0 0 18px; letter-spacing: -0.02em; }
  /* Pages should break cleanly between top-level blocks. */
  @media print {
    .bn-block-content > .bn-inline-content { page-break-inside: avoid; }
  }
</style>
</head>
<body>
<div class="print-root">
  <h1 class="print-title">${escapeHtml(title)}</h1>
  <div id="print-payload"></div>
</div>
<script>
  // Wait one paint to let stylesheets load, then drive print and close
  // the window when the user dismisses the dialog.
  window.addEventListener('load', function () {
    requestAnimationFrame(function () {
      requestAnimationFrame(function () {
        window.focus();
        window.print();
      });
    });
  });
  window.addEventListener('afterprint', function () { window.close(); });
</script>
</body>
</html>`;

  const printWindow = window.open("", "_blank", "width=900,height=1000");
  if (!printWindow) {
    console.warn("[exportToPdf] Popup blocked; can't open print window.");
    return;
  }
  printWindow.document.open();
  printWindow.document.write(printDoc);
  printWindow.document.close();
  // Inject the cloned editor DOM after the doc is written so any inline
  // refs in the source HTML resolve. We replace the placeholder div.
  const onReady = () => {
    const slot = printWindow.document.getElementById("print-payload");
    if (slot) slot.appendChild(cloned);
  };
  if (printWindow.document.readyState === "complete") {
    onReady();
  } else {
    printWindow.addEventListener("load", onReady, { once: true });
  }
}

function escapeHtml(text: string): string {
  return text
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}
