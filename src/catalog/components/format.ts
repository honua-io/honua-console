const DATE_FORMAT = new Intl.DateTimeFormat(undefined, {
  year: "numeric",
  month: "short",
  day: "numeric",
});

export function formatDate(iso: string | null | undefined): string {
  if (!iso) return "—";
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return iso;
  return DATE_FORMAT.format(parsed);
}

export function paragraphs(text: string | null | undefined): readonly string[] {
  if (!text) return [];
  return text
    .replace(/\r\n/g, "\n")
    .split(/\n\n+/)
    .map((p) =>
      p
        .replace(/^\n+|\n+$/g, "")
        .replace(/\n+/g, " ")
        .trim(),
    )
    .filter((p) => p.length > 0);
}
