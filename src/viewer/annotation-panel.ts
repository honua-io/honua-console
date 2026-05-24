import type { AnnotationWorkspaceState } from "../saved-maps/types.js";
import type { AnnotationExportFormat } from "./annotation-export.js";
import {
  type AnnotationModerationState,
  type PortalAnnotationThread,
  type PortalShapeAnnotation,
  countOpenThreads,
  getAnnotationPins,
  getAnnotationThreads,
  getPointAnnotations,
  getShapeAnnotations,
} from "./annotation-state.js";
import type { SelectedFeature } from "./types.js";

export interface AnnotationPanelOptions {
  onPlaceMapPin: (body: string) => void;
  onPlaceRectangle: (title: string) => void;
  onStartPolygon: (title: string) => void;
  onFinishPolygon: () => void;
  onCancelPolygon: () => void;
  onStartFreehand: (title: string) => void;
  onFinishFreehand: () => void;
  onCancelFreehand: () => void;
  onAddFeatureThread: (body: string) => void;
  onAddReply: (threadId: string, body: string) => void;
  onSetThreadStatus: (threadId: string, status: "open" | "resolved") => void;
  onSetThreadModeration: (threadId: string, state: AnnotationModerationState) => void;
  onSetPublicComments: (enabled: boolean) => void;
  onSelectThread: (threadId: string) => void;
  onExport: (format: AnnotationExportFormat) => void;
}

export interface AnnotationPanelRenderInput {
  workspace?: AnnotationWorkspaceState;
  mode: "edit" | "readonly" | "public-comment" | "unavailable";
  selectedFeature?: SelectedFeature;
  selectedThreadId?: string;
  pendingPlacement?: boolean;
  pendingPlacementKind?: "pin" | "rectangle" | "polygon" | "freehand";
  pendingPlacementCopy?: string;
  polygonDraftTitle?: string;
  polygonDraftVertexCount?: number;
  freehandDraftTitle?: string;
  freehandDraftPointCount?: number;
  canModerate?: boolean;
  canExport?: boolean;
  message?: string;
}

export interface AnnotationPanel {
  render: (input: AnnotationPanelRenderInput) => void;
}

export function createAnnotationPanel(host: HTMLElement, options: AnnotationPanelOptions): AnnotationPanel {
  function render(input: AnnotationPanelRenderInput): void {
    host.innerHTML = "";
    host.appendChild(renderSummary(input));

    if (input.mode === "unavailable") {
      host.appendChild(paragraph(input.message ?? "Annotations are not available for this saved map.", "empty-copy"));
      return;
    }

    if (input.mode === "edit") {
      if (input.canModerate) host.appendChild(renderPolicyControls(input));
      host.appendChild(renderComposer(input));
      if (input.canExport) host.appendChild(renderExportActions(input));
    } else if (input.mode === "public-comment") {
      host.appendChild(renderPublicComposer(input));
    } else {
      host.appendChild(
        paragraph("Annotations follow this saved map's sharing settings.", "annotation-panel__readonly"),
      );
    }

    host.appendChild(renderThreadList(input));
  }

  function renderSummary(input: AnnotationPanelRenderInput): HTMLElement {
    const workspace = input.workspace;
    const readOptions = input.mode === "public-comment" ? { audience: "public" as const } : {};
    const annotations = workspace ? getAnnotationPins(workspace, readOptions) : [];
    const shapes = workspace ? getShapeAnnotations(workspace) : [];
    const summary = document.createElement("div");
    summary.className = "annotation-panel__summary";
    summary.appendChild(metric(annotations.length.toString(), annotations.length === 1 ? "Pin" : "Pins"));
    summary.appendChild(metric(shapes.length.toString(), shapes.length === 1 ? "Shape" : "Shapes"));
    summary.appendChild(metric(countOpenThreads(workspace ?? emptyWorkspace(), readOptions), "Open"));
    if (input.pendingPlacement) {
      const placement = document.createElement("span");
      placement.className = "annotation-panel__placement";
      placement.textContent = input.pendingPlacementCopy ?? "Click the map to place the pin";
      summary.appendChild(placement);
    }
    if (input.message && input.mode !== "unavailable") {
      const message = document.createElement("span");
      message.className = "annotation-panel__message";
      message.textContent = input.message;
      summary.appendChild(message);
    }
    return summary;
  }

  function renderPolicyControls(input: AnnotationPanelRenderInput): HTMLElement {
    const wrapper = document.createElement("div");
    wrapper.className = "annotation-panel__policy";

    const label = document.createElement("label");
    label.className = "annotation-panel__check";
    const checkbox = document.createElement("input");
    checkbox.type = "checkbox";
    checkbox.checked = input.workspace?.visibility.publicComments ?? false;
    checkbox.addEventListener("change", () => options.onSetPublicComments(checkbox.checked));
    const text = document.createElement("span");
    text.textContent = "Allow public comments";
    label.append(checkbox, text);

    const state = document.createElement("span");
    state.className = "annotation-panel__policy-state";
    state.textContent = checkbox.checked ? "Guest comments require approval" : "Embed comments disabled";
    wrapper.append(label, state);
    return wrapper;
  }

  function renderComposer(input: AnnotationPanelRenderInput): HTMLElement {
    const isPolygonDraft = input.pendingPlacementKind === "polygon";
    const isFreehandDraft = input.pendingPlacementKind === "freehand";
    const form = document.createElement("form");
    form.className = "annotation-panel__composer";
    form.addEventListener("submit", (event) => event.preventDefault());

    const label = document.createElement("label");
    label.className = "annotation-panel__field";
    const labelText = document.createElement("span");
    labelText.textContent = "Comment";
    const textarea = document.createElement("textarea");
    textarea.name = "annotation-body";
    textarea.rows = 3;
    textarea.maxLength = 1000;
    textarea.placeholder = "Add a map note, feature comment, or shape label";
    textarea.setAttribute("data-annotation-body", "");
    const draftTitle = input.polygonDraftTitle ?? input.freehandDraftTitle;
    if (draftTitle) textarea.value = draftTitle;
    textarea.disabled = isPolygonDraft || isFreehandDraft;
    label.append(labelText, textarea);

    const actions = document.createElement("div");
    actions.className = "annotation-panel__actions";

    const pinButton = document.createElement("button");
    pinButton.type = "button";
    pinButton.className = "annotation-panel__action";
    pinButton.textContent = input.pendingPlacementKind === "pin" ? "Placing pin" : "Place pin";
    pinButton.disabled = !!input.pendingPlacement;
    pinButton.addEventListener("click", () => options.onPlaceMapPin(textarea.value));

    const rectangleButton = document.createElement("button");
    rectangleButton.type = "button";
    rectangleButton.className = "annotation-panel__action";
    rectangleButton.textContent = input.pendingPlacementKind === "rectangle" ? "Placing rectangle" : "Place rectangle";
    rectangleButton.disabled = !!input.pendingPlacement;
    rectangleButton.addEventListener("click", () => options.onPlaceRectangle(textarea.value));

    const polygonButton = document.createElement("button");
    polygonButton.type = "button";
    polygonButton.className = "annotation-panel__action";
    polygonButton.textContent = "Start polygon";
    polygonButton.disabled = !!input.pendingPlacement;
    polygonButton.addEventListener("click", () => options.onStartPolygon(textarea.value));

    const freehandButton = document.createElement("button");
    freehandButton.type = "button";
    freehandButton.className = "annotation-panel__action";
    freehandButton.textContent = "Start freehand";
    freehandButton.disabled = !!input.pendingPlacement;
    freehandButton.addEventListener("click", () => options.onStartFreehand(textarea.value));

    const featureButton = document.createElement("button");
    featureButton.type = "button";
    featureButton.className = "annotation-panel__action";
    featureButton.textContent = "Comment on feature";
    featureButton.disabled = !input.selectedFeature || !!input.pendingPlacement;
    featureButton.addEventListener("click", () => options.onAddFeatureThread(textarea.value));

    actions.append(pinButton, rectangleButton, polygonButton, freehandButton, featureButton);
    if (isPolygonDraft) actions.append(renderPolygonDraftActions(input));
    if (isFreehandDraft) actions.append(renderFreehandDraftActions(input));
    form.append(label, actions);
    return form;
  }

  function renderPublicComposer(input: AnnotationPanelRenderInput): HTMLElement {
    const form = document.createElement("form");
    form.className = "annotation-panel__composer";
    form.addEventListener("submit", (event) => event.preventDefault());

    const label = document.createElement("label");
    label.className = "annotation-panel__field";
    const labelText = document.createElement("span");
    labelText.textContent = "Public comment";
    const textarea = document.createElement("textarea");
    textarea.name = "annotation-body";
    textarea.rows = 3;
    textarea.maxLength = 1000;
    textarea.placeholder = "Add a public map note for review";
    textarea.setAttribute("data-annotation-body", "");
    textarea.disabled = !!input.pendingPlacement;
    label.append(labelText, textarea);

    const actions = document.createElement("div");
    actions.className = "annotation-panel__actions";
    const pinButton = document.createElement("button");
    pinButton.type = "button";
    pinButton.className = "annotation-panel__action";
    pinButton.textContent = input.pendingPlacementKind === "pin" ? "Placing public comment" : "Place public comment";
    pinButton.disabled = !!input.pendingPlacement;
    pinButton.addEventListener("click", () => options.onPlaceMapPin(textarea.value));
    actions.append(pinButton);

    form.append(label, actions);
    return form;
  }

  function renderPolygonDraftActions(input: AnnotationPanelRenderInput): DocumentFragment {
    const fragment = document.createDocumentFragment();
    const finishButton = document.createElement("button");
    finishButton.type = "button";
    finishButton.className = "annotation-panel__action";
    finishButton.textContent = "Finish polygon";
    finishButton.disabled = (input.polygonDraftVertexCount ?? 0) < 3;
    finishButton.addEventListener("click", () => options.onFinishPolygon());

    const cancelButton = document.createElement("button");
    cancelButton.type = "button";
    cancelButton.className = "annotation-panel__action annotation-panel__action--subtle";
    cancelButton.textContent = "Cancel polygon";
    cancelButton.addEventListener("click", () => options.onCancelPolygon());

    fragment.append(finishButton, cancelButton);
    return fragment;
  }

  function renderFreehandDraftActions(input: AnnotationPanelRenderInput): DocumentFragment {
    const fragment = document.createDocumentFragment();
    const finishButton = document.createElement("button");
    finishButton.type = "button";
    finishButton.className = "annotation-panel__action";
    finishButton.textContent = "Finish freehand";
    finishButton.disabled = (input.freehandDraftPointCount ?? 0) < 2;
    finishButton.addEventListener("click", () => options.onFinishFreehand());

    const cancelButton = document.createElement("button");
    cancelButton.type = "button";
    cancelButton.className = "annotation-panel__action annotation-panel__action--subtle";
    cancelButton.textContent = "Cancel freehand";
    cancelButton.addEventListener("click", () => options.onCancelFreehand());

    fragment.append(finishButton, cancelButton);
    return fragment;
  }

  function renderExportActions(input: AnnotationPanelRenderInput): HTMLElement {
    const wrapper = document.createElement("div");
    wrapper.className = "annotation-panel__exports";
    const threads = input.workspace ? getAnnotationThreads(input.workspace) : [];
    const annotations = input.workspace ? getPointAnnotations(input.workspace) : [];
    const shapes = input.workspace ? getShapeAnnotations(input.workspace) : [];
    const disabled = threads.length === 0 && annotations.length === 0 && shapes.length === 0;

    const jsonButton = document.createElement("button");
    jsonButton.type = "button";
    jsonButton.className = "annotation-panel__action annotation-panel__action--subtle";
    jsonButton.textContent = "Export JSON";
    jsonButton.disabled = disabled;
    jsonButton.addEventListener("click", () => options.onExport("json"));

    const geoJsonButton = document.createElement("button");
    geoJsonButton.type = "button";
    geoJsonButton.className = "annotation-panel__action annotation-panel__action--subtle";
    geoJsonButton.textContent = "Export GeoJSON";
    geoJsonButton.disabled = disabled;
    geoJsonButton.addEventListener("click", () => options.onExport("geojson"));

    wrapper.append(jsonButton, geoJsonButton);
    return wrapper;
  }

  function renderThreadList(input: AnnotationPanelRenderInput): HTMLElement {
    const list = document.createElement("div");
    list.className = "annotation-panel__threads";
    const threads = input.workspace
      ? getAnnotationThreads(input.workspace, input.mode === "public-comment" ? { audience: "public" } : {})
      : [];
    const shapes = input.workspace ? getShapeAnnotations(input.workspace) : [];
    if (threads.length === 0 && shapes.length === 0) {
      list.appendChild(
        paragraph(input.mode === "public-comment" ? "No approved comments yet." : "No annotations yet.", "empty-copy"),
      );
      return list;
    }

    for (const thread of threads) {
      list.appendChild(renderThread(thread, input));
    }
    for (const shape of shapes) {
      list.appendChild(renderShape(shape));
    }
    return list;
  }

  function renderThread(thread: PortalAnnotationThread, input: AnnotationPanelRenderInput): HTMLElement {
    const article = document.createElement("article");
    article.className = "annotation-thread";
    article.dataset["threadId"] = thread.id;
    if (input.selectedThreadId === thread.id) article.dataset["selected"] = "true";

    const header = document.createElement("header");
    header.className = "annotation-thread__header";
    const titleButton = document.createElement("button");
    titleButton.type = "button";
    titleButton.className = "annotation-thread__title";
    titleButton.textContent = thread.title;
    titleButton.addEventListener("click", () => options.onSelectThread(thread.id));

    const status = document.createElement("span");
    status.className = "annotation-thread__status";
    status.dataset["status"] = thread.status;
    status.textContent = thread.status === "open" ? "Open" : "Resolved";
    header.append(titleButton, status);
    if (thread.moderation.state !== "approved") {
      const moderation = document.createElement("span");
      moderation.className = "annotation-thread__status annotation-thread__status--moderation";
      moderation.dataset["moderation"] = thread.moderation.state;
      moderation.textContent = moderationLabel(thread.moderation.state);
      header.appendChild(moderation);
    }

    const anchor = document.createElement("p");
    anchor.className = "annotation-thread__anchor";
    anchor.textContent = anchorLabel(thread);

    const comments = document.createElement("ol");
    comments.className = "annotation-thread__comments";
    for (const comment of thread.comments) {
      const item = document.createElement("li");
      const body = document.createElement("p");
      body.textContent = comment.body;
      const meta = document.createElement("span");
      meta.textContent = `${comment.author.name ?? comment.author.id} · ${formatDate(comment.createdAt)}`;
      item.append(body, meta);
      comments.appendChild(item);
    }

    article.append(header, anchor, comments);
    if (input.mode === "edit") article.appendChild(renderThreadActions(thread, input));
    return article;
  }

  function renderShape(shape: PortalShapeAnnotation): HTMLElement {
    const article = document.createElement("article");
    article.className = "annotation-thread annotation-shape";
    article.dataset["shapeId"] = shape.id;

    const header = document.createElement("header");
    header.className = "annotation-thread__header";
    const title = document.createElement("span");
    title.className = "annotation-shape__title";
    title.textContent = shape.title;

    const status = document.createElement("span");
    status.className = "annotation-thread__status";
    status.dataset["status"] = shape.status;
    status.textContent = shape.status === "open" ? "Open" : "Resolved";
    header.append(title, status);

    const anchor = document.createElement("p");
    anchor.className = "annotation-thread__anchor";
    anchor.textContent = shapeLabel(shape.shape);

    const meta = document.createElement("p");
    meta.className = "annotation-shape__meta";
    meta.textContent = `${shape.createdBy.name ?? shape.createdBy.id} · ${formatDate(shape.createdAt)}`;

    article.append(header, anchor, meta);
    return article;
  }

  function renderThreadActions(thread: PortalAnnotationThread, input: AnnotationPanelRenderInput): HTMLElement {
    const wrapper = document.createElement("div");
    wrapper.className = "annotation-thread__actions";

    const replyLabel = document.createElement("label");
    replyLabel.className = "annotation-thread__reply";
    const replyText = document.createElement("span");
    replyText.textContent = "Reply";
    const replyInput = document.createElement("textarea");
    replyInput.rows = 2;
    replyInput.maxLength = 1000;
    replyInput.disabled = thread.status === "resolved";
    replyInput.setAttribute("data-annotation-reply", thread.id);
    replyLabel.append(replyText, replyInput);

    const buttons = document.createElement("div");
    buttons.className = "annotation-panel__actions";

    const replyButton = document.createElement("button");
    replyButton.type = "button";
    replyButton.className = "annotation-panel__action";
    replyButton.textContent = "Reply";
    replyButton.disabled = thread.status === "resolved";
    replyButton.addEventListener("click", () => options.onAddReply(thread.id, replyInput.value));

    const statusButton = document.createElement("button");
    statusButton.type = "button";
    statusButton.className = "annotation-panel__action annotation-panel__action--subtle";
    statusButton.textContent = thread.status === "open" ? "Resolve" : "Reopen";
    statusButton.addEventListener("click", () =>
      options.onSetThreadStatus(thread.id, thread.status === "open" ? "resolved" : "open"),
    );

    buttons.append(replyButton, statusButton);
    if (input.canModerate) {
      if (thread.moderation.state !== "approved") {
        const approveButton = document.createElement("button");
        approveButton.type = "button";
        approveButton.className = "annotation-panel__action";
        approveButton.textContent = "Approve";
        approveButton.addEventListener("click", () => options.onSetThreadModeration(thread.id, "approved"));
        buttons.appendChild(approveButton);
      }
      if (thread.moderation.state !== "hidden") {
        const hideButton = document.createElement("button");
        hideButton.type = "button";
        hideButton.className = "annotation-panel__action annotation-panel__action--subtle";
        hideButton.textContent = "Hide";
        hideButton.addEventListener("click", () => options.onSetThreadModeration(thread.id, "hidden"));
        buttons.appendChild(hideButton);
      }
    }
    wrapper.append(replyLabel, buttons);
    return wrapper;
  }

  return { render };
}

function shapeLabel(shape: PortalShapeAnnotation["shape"]): string {
  switch (shape) {
    case "rectangle":
      return "Rectangle drawing";
    case "polygon":
      return "Polygon drawing";
    case "freehand":
      return "Freehand drawing";
  }
}

function moderationLabel(state: AnnotationModerationState): string {
  switch (state) {
    case "approved":
      return "Approved";
    case "pending":
      return "Pending approval";
    case "hidden":
      return "Hidden";
  }
}

function metric(value: string | number, label: string): HTMLElement {
  const element = document.createElement("span");
  element.className = "annotation-panel__metric";
  const valueElement = document.createElement("strong");
  valueElement.textContent = String(value);
  const labelElement = document.createElement("span");
  labelElement.textContent = label;
  element.append(valueElement, labelElement);
  return element;
}

function paragraph(text: string, className: string): HTMLParagraphElement {
  const p = document.createElement("p");
  p.className = className;
  p.textContent = text;
  return p;
}

function anchorLabel(thread: PortalAnnotationThread): string {
  if (thread.anchor.kind === "map") {
    return `Map pin at ${thread.anchor.lngLat[1].toFixed(5)}, ${thread.anchor.lngLat[0].toFixed(5)}`;
  }
  return thread.anchor.label ?? `Feature ${thread.anchor.featureId}`;
}

function formatDate(value: string): string {
  if (!value) return "Unknown date";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleDateString(undefined, { month: "short", day: "numeric", year: "numeric" });
}

function emptyWorkspace(): AnnotationWorkspaceState {
  return {
    version: "honua-annotations/v1",
    visibility: { defaultAudience: "map", publicComments: false },
    annotationSets: [],
    commentThreads: [],
  };
}
