import { useEffect, useRef, useState } from "react";

export interface SearchBarProps {
  readonly value: string;
  readonly onChange: (value: string) => void;
  readonly debounceMs?: number;
}

export function SearchBar({ value, onChange, debounceMs = 300 }: SearchBarProps) {
  const [draft, setDraft] = useState(value);
  const onChangeRef = useRef(onChange);
  onChangeRef.current = onChange;

  useEffect(() => {
    setDraft(value);
  }, [value]);

  useEffect(() => {
    if (draft === value) return;
    const handle = setTimeout(() => {
      onChangeRef.current(draft);
    }, debounceMs);
    return () => clearTimeout(handle);
  }, [draft, value, debounceMs]);

  return (
    <label className="search-bar">
      <span className="search-bar__label">Search catalog</span>
      <input
        type="search"
        className="search-bar__input"
        placeholder="Search by title, tag, owner…"
        value={draft}
        onChange={(event) => setDraft(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === "Enter") {
            event.preventDefault();
            onChangeRef.current(draft);
          }
        }}
        aria-label="Search catalog"
      />
    </label>
  );
}
