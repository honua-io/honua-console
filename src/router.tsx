import { Route, Routes } from "react-router-dom";

import { AREA_DESCRIPTORS, CONSOLE_AREAS } from "./areas";
import AreaPlaceholder from "./routes/AreaPlaceholder";
import Home from "./routes/Home";
import NotFound from "./routes/NotFound";

export function AppRoutes(): JSX.Element {
  return (
    <Routes>
      <Route path="/" element={<Home />} />
      {CONSOLE_AREAS.map((id) => {
        const area = AREA_DESCRIPTORS[id];
        return <Route key={id} path={`${area.path}/*`} element={<AreaPlaceholder area={area} />} />;
      })}
      <Route path="*" element={<NotFound />} />
    </Routes>
  );
}
