import js from "@eslint/js";
import tseslint from "typescript-eslint";
import reactPlugin from "eslint-plugin-react";
import reactHooks from "eslint-plugin-react-hooks";
import jsxA11y from "eslint-plugin-jsx-a11y";
import eslintComments from "@eslint-community/eslint-plugin-eslint-comments";
import globals from "globals";

// Initial ESLint config — primarily wired up for accessibility (jsx-a11y)
// signal. Rules are intentionally pitched as warnings so a fresh `npm run
// lint` doesn't break CI on a backlog of existing violations; tighten to
// errors as the codebase converges.
//
// React + react-hooks rule sets stay close to recommended so we get the
// usual missing-dep and incorrect-key warnings for free.
//
// The TypeScript-ESLint flat-config preset handles parser setup for
// .ts/.tsx — no manual `parser` or `parserOptions` block needed here.
export default [
  {
    ignores: [
      "dist/**",
      "node_modules/**",
      "vite.config.*.timestamp-*",
      "scripts/fetch-drawio.mjs"
    ]
  },
  {
    // Apply browser + node globals everywhere; the SPA is a browser build
    // and a few files (vite.config.ts, scripts) run in Node.
    languageOptions: {
      globals: { ...globals.browser, ...globals.node }
    }
  },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    files: ["**/*.{ts,tsx,jsx,js,mjs}"],
    plugins: {
      react: reactPlugin,
      "react-hooks": reactHooks,
      "jsx-a11y": jsxA11y,
      "eslint-comments": eslintComments
    },
    languageOptions: {
      globals: { ...globals.browser, ...globals.node },
      parserOptions: {
        ecmaFeatures: { jsx: true }
      }
    },
    settings: {
      react: { version: "detect" }
    },
    rules: {
      ...reactPlugin.configs.flat.recommended.rules,
      ...reactPlugin.configs.flat["jsx-runtime"].rules,
      // react-hooks v7 ships several new analyses (set-state-in-effect,
      // ref-access-during-render, immutability) that fire heavily on
      // existing code patterns. Adopt the conservative legacy preset so
      // the lint run is useful day one; tighten as we go.
      "react-hooks/rules-of-hooks": "error",
      "react-hooks/exhaustive-deps": "warn",
      ...jsxA11y.flatConfigs.recommended.rules,

      // ── React: defaults overridden ───────────────────────────────────────
      // The codebase imports React only where it uses runtime APIs; the new
      // JSX transform doesn't need a React import.
      "react/react-in-jsx-scope": "off",
      // prop-types is irrelevant in TypeScript code.
      "react/prop-types": "off",
      // Existing code has hundreds of un-escaped `'` and `"` in JSX text.
      // Stylistic, not a bug — drop to warn so the initial pass isn't all
      // noise.
      "react/no-unescaped-entities": "warn",
      "react/display-name": "off",
      "react/no-unknown-property": "warn",
      "react/jsx-key": "warn",

      // ── TypeScript noise downgraded so the initial lint pass is useful ──
      // Existing code uses `any` in places (JSON parsing, dynamic forms);
      // dial these down rather than break the world on day one.
      "@typescript-eslint/no-explicit-any": "off",
      "@typescript-eslint/no-unused-vars": ["warn", { argsIgnorePattern: "^_", varsIgnorePattern: "^_" }],
      "no-unused-vars": "off",
      "@typescript-eslint/no-empty-object-type": "off",

      // ── Accessibility (the reason this config exists) ───────────────────
      // Most of these come from jsx-a11y's recommended preset above; the
      // overrides keep the load-bearing ones at warn (visible in IDE,
      // doesn't break CI) so we can knock the backlog down incrementally.
      "jsx-a11y/alt-text": "warn",
      "jsx-a11y/anchor-has-content": "warn",
      "jsx-a11y/anchor-is-valid": "warn",
      "jsx-a11y/click-events-have-key-events": "warn",
      "jsx-a11y/label-has-associated-control": "warn",
      "jsx-a11y/no-static-element-interactions": "warn",
      "jsx-a11y/no-noninteractive-element-interactions": "warn",
      "jsx-a11y/no-noninteractive-tabindex": "warn",
      "jsx-a11y/no-autofocus": "warn",

      // ── Non-a11y errors that are stylistic, not bugs ────────────────────
      // Real-bug rules stay on as errors; these are codebase-wide stylistic
      // patterns that we don't want a lint pass to block on day one.
      "no-useless-escape": "warn",
      "no-extra-boolean-cast": "warn",
      "no-empty": ["warn", { allowEmptyCatch: true }],

      // ── Suppressions must say why ───────────────────────────────────────
      // A bare `eslint-disable-next-line react-hooks/exhaustive-deps` is
      // indistinguishable from a stale-closure bug someone silenced (#32), and
      // the whole point of a suppression is that a human decided it was safe —
      // so the decision has to be written down. Error, not warn: this is
      // cheap to satisfy at the moment you add the directive, and the repo is
      // currently at zero.
      "eslint-comments/require-description": ["error", { ignore: [] }]
    }
  },

  // ── Accessibility ratchet (#40) ─────────────────────────────────────────
  //
  // The rules above are warnings inside a total budget, so a new violation is
  // free until the budget runs out — which makes every 508 fix a thing that
  // can silently regress. This block re-declares the same rules as ERRORS for
  // the directories that are already clean, so those areas cannot go
  // backwards while the remaining backlog is worked through.
  //
  // Adding a directory here is the ratchet: fix a directory's warnings, move
  // it into this list, and it stays fixed. The list below was derived by
  // running eslint and taking every directory with zero jsx-a11y warnings —
  // not by aspiration, so `npm run lint` passes the moment it lands.
  //
  // Remaining (deliberately absent): src/pages/notes, src/pages/documents,
  // src/pages/admin, src/components/documents, src/agent, src/shell,
  // src/pages/workflow*, src/pages/dashboard, and three single-file cases in
  // src/components. Most are keyboard-interaction findings on editor
  // surfaces, which need real interaction design rather than a lint fix.
  {
    files: [
      "src/components/data-table/**/*.{ts,tsx}",
      "src/pages/records/**/*.{ts,tsx}",
      "src/pages/notifications/**/*.{ts,tsx}",
      "src/pages/query/**/*.{ts,tsx}",
      "src/pages/forms/**/*.{ts,tsx}",
      "src/pages/manage-users/**/*.{ts,tsx}",
      "src/pages/user-profile/**/*.{ts,tsx}",
      "src/pages/login/**/*.{ts,tsx}",
      "src/pages/home/**/*.{ts,tsx}",
      "src/widgets/**/*.{ts,tsx}",
      "src/menus/**/*.{ts,tsx}",
      "src/providers/**/*.{ts,tsx}",
      "src/routes/**/*.{ts,tsx}",
      "src/preferences/**/*.{ts,tsx}",
      "src/hooks/**/*.{ts,tsx}",
      "src/lib/**/*.{ts,tsx}"
    ],
    rules: {
      "jsx-a11y/alt-text": "error",
      "jsx-a11y/anchor-has-content": "error",
      "jsx-a11y/anchor-is-valid": "error",
      "jsx-a11y/click-events-have-key-events": "error",
      "jsx-a11y/label-has-associated-control": "error",
      "jsx-a11y/no-static-element-interactions": "error",
      "jsx-a11y/no-noninteractive-element-interactions": "error",
      "jsx-a11y/no-noninteractive-tabindex": "error",
      "jsx-a11y/no-autofocus": "error"
    }
  }
];
