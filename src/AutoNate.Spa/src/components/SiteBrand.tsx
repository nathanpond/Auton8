import { useEffect, useState } from "react";
import { SiteAppearance } from "@/types/siteAppearance";

type SiteBrandProps = {
  appearance: SiteAppearance;
  className?: string;
  iconClassName?: string;
  textClassName?: string;
  imageClassName?: string;
};

export default function SiteBrand({
  appearance,
  className,
  iconClassName,
  textClassName,
  imageClassName
}: SiteBrandProps) {
  const [imageFailed, setImageFailed] = useState(false);

  useEffect(() => {
    setImageFailed(false);
  }, [appearance.logoImageUrl, appearance.logoMode]);

  const showImage =
    appearance.logoMode === "image" &&
    !!appearance.logoImageUrl &&
    !imageFailed;

  return (
    <span className={className}>
      {showImage ? (
        <img
          src={appearance.logoImageUrl ?? undefined}
          alt={appearance.siteName}
          className={imageClassName}
          style={{
            display: "block",
            maxHeight: "36px",
            maxWidth: "180px",
            objectFit: "contain"
          }}
          onError={() => setImageFailed(true)}
        />
      ) : (
        <>
          <span className={iconClassName}>
            <i className={appearance.logoIcon ?? "fa fa-robot"} />
          </span>
          <span className={textClassName}>{appearance.logoText}</span>
        </>
      )}
    </span>
  );
}
