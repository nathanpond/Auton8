# Provider logos

`ProviderLogo` (in `src/components/agent/ProviderLogo.tsx`) renders the icon
shown next to each model in the Site Configuration → Chatbot → Models page.
For any given provider it first tries to load:

```
/assets/img/providers/<lowercased-provider-name>.svg
```

If the file is missing the component falls back to a colored monogram in the
provider's brand color. That's why the page works out of the box without any
files in this directory.

## Adding a real logo

Drop a square SVG into this directory using a lowercased file name that
matches the value of `agent_model.provider` for that vendor:

| `agent_model.provider` | File name in this directory |
|------------------------|-----------------------------|
| `Anthropic`            | `anthropic.svg`             |
| `OpenAI`               | `openai.svg`                |

Recommended specs:

- Square viewBox (the component renders the image at 24×24 by default; the
  `objectFit: contain` style keeps non-square marks from distorting).
- Transparent background — the table cell already provides one.
- Single-color or full-color is fine; the icon sits on a neutral row.

## Where to source the logos

Both vendors publish brand/press kits and trademark guidelines. Use those —
don't grab a random SVG off the web — so your deployment respects their
trademark policies:

- Anthropic — start at [anthropic.com](https://www.anthropic.com/) and look
  for "brand", "press kit", or "media kit". Trademark guidance covers what
  you're allowed to do with the mark; the typical product-integration use
  case (showing "this is the model from Anthropic") is generally permitted
  but read the policy before shipping.
- OpenAI — start at [openai.com](https://openai.com/) under "brand
  guidelines" / press resources. Same caveat: read the trademark policy.

If the vendor offers multiple variants (full wordmark, monogram only, dark/
light), prefer the **monogram** since the column is narrow.

## Adding more providers

The component already handles unknown providers — they just render the gray
monogram. To give a new provider a brand-colored monogram fallback, add an
entry to `styleFor` in `ProviderLogo.tsx` with their brand color and letter.
To use a real logo for them, just drop `<provider>.svg` here using the rules
above.
