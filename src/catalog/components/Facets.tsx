import {
  type ContentItemSummary,
  ITEM_TYPES,
  type ItemType,
  SHARING_LEVELS,
  type Sharing,
} from "../../contracts/content-item.js";
import { typeLabel } from "../../ui/TypePill.js";
import { visibilityLabel } from "../../ui/VisibilityPill.js";

export interface FacetsProps {
  readonly items: readonly ContentItemSummary[];
  readonly selectedType: ItemType | null;
  readonly selectedTag: string | null;
  readonly selectedOwner: string | null;
  readonly selectedVisibility: Sharing | null;
  readonly onChange: (next: {
    type?: ItemType | null;
    tag?: string | null;
    owner?: string | null;
    visibility?: Sharing | null;
  }) => void;
}

const TOP_TAG_LIMIT = 8;
const TOP_OWNER_LIMIT = 6;

export function Facets({ items, selectedType, selectedTag, selectedOwner, selectedVisibility, onChange }: FacetsProps) {
  const tagBuckets = bucketize(items.flatMap((item) => item.tags));
  const ownerBuckets = bucketize(items.map((item) => `${item.owner.id}::${item.owner.name}`));

  return (
    <aside className="facets" aria-label="Catalog filters">
      <FacetGroup title="Item type">
        {ITEM_TYPES.map((type) => (
          <FacetToggle
            key={type}
            label={typeLabel(type)}
            checked={selectedType === type}
            onToggle={() => onChange({ type: selectedType === type ? null : type })}
          />
        ))}
      </FacetGroup>

      {tagBuckets.length > 0 ? (
        <FacetGroup title="Tag">
          {tagBuckets.slice(0, TOP_TAG_LIMIT).map(({ key, count }) => (
            <FacetToggle
              key={key}
              label={`${key} (${count})`}
              checked={selectedTag === key}
              onToggle={() => onChange({ tag: selectedTag === key ? null : key })}
            />
          ))}
        </FacetGroup>
      ) : null}

      {ownerBuckets.length > 0 ? (
        <FacetGroup title="Owner">
          {ownerBuckets.slice(0, TOP_OWNER_LIMIT).map(({ key, count }) => {
            const [id, name] = key.split("::");
            return (
              <FacetToggle
                key={key}
                label={`${name} (${count})`}
                checked={selectedOwner === id}
                onToggle={() => onChange({ owner: selectedOwner === id ? null : (id ?? null) })}
              />
            );
          })}
        </FacetGroup>
      ) : null}

      <FacetGroup title="Visibility">
        {SHARING_LEVELS.map((level) => (
          <FacetToggle
            key={level}
            label={visibilityLabel(level)}
            checked={selectedVisibility === level}
            onToggle={() => onChange({ visibility: selectedVisibility === level ? null : level })}
          />
        ))}
      </FacetGroup>
    </aside>
  );
}

function FacetGroup({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <fieldset className="facets__group">
      <legend className="facets__legend">{title}</legend>
      <ul className="facets__list">{children}</ul>
    </fieldset>
  );
}

function FacetToggle({
  label,
  checked,
  onToggle,
}: {
  label: string;
  checked: boolean;
  onToggle: () => void;
}) {
  return (
    <li className="facets__item">
      <label className={`facets__option${checked ? " facets__option--checked" : ""}`}>
        <input type="checkbox" checked={checked} onChange={onToggle} />
        <span>{label}</span>
      </label>
    </li>
  );
}

function bucketize(values: readonly string[]): Array<{ key: string; count: number }> {
  const counts = new Map<string, number>();
  for (const value of values) {
    counts.set(value, (counts.get(value) ?? 0) + 1);
  }
  return [...counts.entries()]
    .map(([key, count]) => ({ key, count }))
    .sort((a, b) => b.count - a.count || a.key.localeCompare(b.key));
}
