import { useState } from "react";

// Compact provider mark for use in tables and badges. Tries to load
// /assets/img/providers/<lowercased-provider>.svg first; if that file
// doesn't ship in the SPA bundle (404 / network error) the component
// gracefully falls back to a colored monogram square in the provider's
// brand color. The HTML `title` attribute drives the hover tooltip in
// both states, and remains the source of truth for what the icon means
// regardless of which path renders.
//
// To replace the monogram with a real brand mark for a given provider,
// drop a square SVG into public/assets/img/providers/<provider>.svg
// (lowercased file name). See public/assets/img/providers/README.md for
// the licensing notes — official brand kits are the safe source.

type ProviderLogoProps = {
  provider: string;
  size?: number;
};

type Style = {
  background: string;
  letter: string;
};

function styleFor(provider: string): Style {
  const key = provider.trim().toLowerCase();
  switch (key) {
    case "anthropic":
      return { background: "#CC785C", letter: "A" };
    case "openai":
      return { background: "#10A37F", letter: "O" };
    default:
      return {
        background: "#6C757D",
        letter: provider ? provider[0].toUpperCase() : "?"
      };
  }
}

function logoUrlFor(provider: string): string {
  const slug = provider.trim().toLowerCase().replace(/[^a-z0-9-]+/g, "-");
  return `/assets/img/providers/${slug}.svg`;
}

export function ProviderLogo({ provider, size = 24 }: ProviderLogoProps) {
  const [imageFailed, setImageFailed] = useState(false);
  const { background, letter } = styleFor(provider);
  const showImage = !imageFailed && provider.trim().length > 0;

  if (showImage) {
    return (
      <img
        src={logoUrlFor(provider)}
        alt={provider}
        title={provider}
        width={size}
        height={size}
        style={{
          display: "inline-block",
          width: size,
          height: size,
          objectFit: "contain",
          borderRadius: 4
        }}
        onError={() => setImageFailed(true)}
      />
    );
  }

  return (
    <span
      title={provider}
      aria-label={provider}
      role="img"
      style={{
        display: "inline-flex",
        alignItems: "center",
        justifyContent: "center",
        width: size,
        height: size,
        borderRadius: 4,
        background,
        color: "white",
        fontWeight: 700,
        fontSize: Math.round(size * 0.55),
        lineHeight: 1,
        userSelect: "none"
      }}
    >
      {letter}
    </span>
  );
}
