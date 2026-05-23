import type { AreaDescriptor } from "../areas";

interface AreaPlaceholderProps {
  area: AreaDescriptor;
}

// Placeholder surface mounted by the deploy-bundle scaffold. Issues #2/#4/#5/#6
// replace this with the real area views; until then the placeholder keeps the
// single-origin route map (/studio, /catalog, /operate, /share) live so devops
// can preview and promote the artifact.
export default function AreaPlaceholder({ area }: AreaPlaceholderProps): JSX.Element {
  return (
    <section data-area={area.id} aria-labelledby={`${area.id}-heading`} style={{ padding: "24px" }}>
      <h1 id={`${area.id}-heading`}>{area.label}</h1>
      <p>{area.summary}</p>
      <p>
        This area is scaffolded by the deploy-bundle ticket and will be replaced by the area-owning ticket. The route is
        live so the single deployable artifact can be previewed end-to-end.
      </p>
    </section>
  );
}
