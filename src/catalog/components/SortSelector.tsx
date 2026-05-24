import { SORT_OPTIONS, type SortOption } from "../../contracts/content-item.js";

const LABELS: Record<SortOption, string> = {
  "modified-desc": "Most recently modified",
  "modified-asc": "Oldest modified first",
  "title-asc": "Title A → Z",
  "title-desc": "Title Z → A",
  relevance: "Relevance",
};

export interface SortSelectorProps {
  readonly value: SortOption;
  readonly onChange: (value: SortOption) => void;
  readonly relevanceEnabled: boolean;
}

export function SortSelector({ value, onChange, relevanceEnabled }: SortSelectorProps) {
  return (
    <label className="sort-selector">
      <span className="sort-selector__label">Sort</span>
      <select
        className="sort-selector__select"
        value={value}
        onChange={(event) => onChange(event.target.value as SortOption)}
        aria-label="Sort results"
      >
        {SORT_OPTIONS.map((option) => (
          <option key={option} value={option} disabled={option === "relevance" && !relevanceEnabled}>
            {LABELS[option]}
          </option>
        ))}
      </select>
    </label>
  );
}
