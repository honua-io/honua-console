import { NavLink } from "react-router-dom";

interface NavGroup {
  readonly area: string;
  readonly items: ReadonlyArray<{ readonly to: string; readonly label: string }>;
}

const GROUPS: ReadonlyArray<NavGroup> = [
  {
    area: "Studio",
    items: [{ to: "/studio/preview", label: "Generated app preview" }],
  },
  {
    area: "Catalog",
    items: [
      { to: "/catalog/items", label: "Content items" },
      { to: "/catalog/packages", label: "Map packages" },
    ],
  },
  {
    area: "Operate",
    items: [{ to: "/operate/provenance", label: "Provenance" }],
  },
  {
    area: "Share",
    items: [{ to: "/share", label: "Sharing policies" }],
  },
];

export function Nav(): JSX.Element {
  return (
    <nav className="console-shell__nav" aria-label="Console areas">
      {GROUPS.map((group) => (
        <section key={group.area}>
          <h1>{group.area}</h1>
          <ul>
            {group.items.map((item) => (
              <li key={item.to}>
                <NavLink to={item.to} className={({ isActive }) => (isActive ? "is-active" : "")}>
                  {item.label}
                </NavLink>
              </li>
            ))}
          </ul>
        </section>
      ))}
    </nav>
  );
}
