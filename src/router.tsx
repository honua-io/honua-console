import { createBrowserRouter } from "react-router-dom";

import { AppShell } from "./shell/AppShell.js";
import { PublishedItemRoutePage } from "./routes/PublishedItemRoutePage.js";
import { StudioDraftPage } from "./routes/StudioDraftPage.js";
import { StudioHomePage } from "./routes/StudioHomePage.js";
import { StudioItemEditPage } from "./routes/StudioItemEditPage.js";
import { StudioPreviewPage } from "./routes/StudioPreviewPage.js";
import { StudioPublishReviewPage } from "./studio/publishing/StudioPublishReviewPage.js";

export const router = createBrowserRouter([
  {
    path: "/",
    element: <AppShell />,
    children: [
      { index: true, element: <StudioHomePage /> },
      { path: "studio", element: <StudioHomePage /> },
      { path: "studio/drafts/:draftId", element: <StudioDraftPage /> },
      { path: "studio/previews/:draftId", element: <StudioPreviewPage /> },
      { path: "studio/drafts/:draftId/publish", element: <StudioPublishReviewPage /> },
      { path: "studio/items/:itemId/edit", element: <StudioItemEditPage /> },
      { path: "catalog/:itemId", element: <PublishedItemRoutePage surface="catalog" /> },
      { path: "maps/:itemId", element: <PublishedItemRoutePage surface="map-preview" /> },
      { path: "dashboards/:itemId", element: <PublishedItemRoutePage surface="dashboard-preview" /> },
      { path: "reports/:itemId", element: <PublishedItemRoutePage surface="report-preview" /> },
      { path: "apps/:itemId/preview", element: <PublishedItemRoutePage surface="app-preview" /> },
      { path: "share/:itemId", element: <PublishedItemRoutePage surface="share" /> },
      { path: "embed/:itemId", element: <PublishedItemRoutePage surface="embed" /> },
      { path: "*", element: <PublishedItemRoutePage surface="missing" /> }
    ]
  }
]);
