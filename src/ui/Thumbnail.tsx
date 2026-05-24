import type { ItemType } from "../contracts/content-item.js";
import { typeLabel } from "./TypePill.js";

export interface ThumbnailProps {
  readonly src: string | null;
  readonly alt: string;
  readonly type: ItemType;
}

export function Thumbnail({ src, alt, type }: ThumbnailProps) {
  if (!src) {
    return (
      <div className="thumbnail thumbnail--empty" role="img" aria-label={`No thumbnail for ${alt}`}>
        <span className="thumbnail__placeholder">{typeLabel(type).charAt(0)}</span>
      </div>
    );
  }
  return (
    <img
      src={src}
      alt={alt}
      loading="lazy"
      decoding="async"
      className="thumbnail"
      onError={(event) => {
        const target = event.currentTarget;
        target.replaceWith(buildPlaceholder(alt, type));
      }}
    />
  );
}

function buildPlaceholder(alt: string, type: ItemType): HTMLElement {
  const div = document.createElement("div");
  div.className = "thumbnail thumbnail--empty";
  div.setAttribute("role", "img");
  div.setAttribute("aria-label", `Thumbnail unavailable for ${alt}`);
  const placeholder = document.createElement("span");
  placeholder.className = "thumbnail__placeholder";
  placeholder.textContent = typeLabel(type).charAt(0);
  div.appendChild(placeholder);
  return div;
}
