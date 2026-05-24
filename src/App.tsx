import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import { lazy, Suspense } from "react";

import "./styles/global.css";

import { CatalogItemsPage } from "./pages/CatalogItemsPage";
import { CatalogPackagesPage } from "./pages/CatalogPackagesPage";
import { OperateProvenancePage } from "./pages/OperateProvenancePage";
import { SharePage } from "./pages/SharePage";
import { ControlPlaneProvider } from "./shell/ControlPlaneProvider";
import { Nav } from "./shell/Nav";
import { SessionBanner } from "./shell/SessionBanner";
import { SessionProvider } from "./session/SessionProvider";

const StudioPreviewPage = lazy(() =>
  import("./pages/StudioPreviewPage").then((module) => ({ default: module.StudioPreviewPage })),
);

export function App(): JSX.Element {
  return (
    <SessionProvider>
      <ControlPlaneProvider>
        <BrowserRouter>
          <div className="console-shell">
            <Nav />
            <main className="console-shell__main">
              <SessionBanner />
              <Routes>
                <Route path="/" element={<Navigate to="/catalog/items" replace />} />
                <Route
                  path="/studio/preview"
                  element={
                    <Suspense fallback={null}>
                      <StudioPreviewPage />
                    </Suspense>
                  }
                />
                <Route path="/catalog/items" element={<CatalogItemsPage />} />
                <Route path="/catalog/packages" element={<CatalogPackagesPage />} />
                <Route path="/operate/provenance" element={<OperateProvenancePage />} />
                <Route path="/share" element={<SharePage />} />
                <Route path="*" element={<Navigate to="/catalog/items" replace />} />
              </Routes>
            </main>
          </div>
        </BrowserRouter>
      </ControlPlaneProvider>
    </SessionProvider>
  );
}
