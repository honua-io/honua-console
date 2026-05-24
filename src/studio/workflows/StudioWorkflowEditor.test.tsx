import { fireEvent, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

import { createStudioWorkflowFixtureClient } from "./fixtureClient";
import { StudioWorkflowEditor } from "./StudioWorkflowEditor";

describe("StudioWorkflowEditor", () => {
  it("walks a builder from prompt to inspected draft, validation, run, and publication", async () => {
    const user = userEvent.setup();
    render(<StudioWorkflowEditor transport={createStudioWorkflowFixtureClient()} />);

    await user.click(screen.getByRole("button", { name: /generate draft/i }));

    expect(await screen.findByText(/Draft ready/i)).toBeInTheDocument();
    expect((screen.getByLabelText(/workflow definition json/i) as HTMLTextAreaElement).value).toContain(
      "geometry.buffer",
    );
    expect(screen.getByText(/Process service eligible/i)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /^validate$/i }));

    expect(await screen.findByText(/^valid$/i)).toBeInTheDocument();
    expect(screen.getByText(/Definition is valid/i)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /dry run/i }));

    const runHistory = await screen.findByLabelText(/run history/i);
    expect(within(runHistory).getAllByText(/successful/i).length).toBeGreaterThan(0);
    expect(within(runHistory).getAllByText(/Rejected Rows/i).length).toBeGreaterThan(0);
    expect(within(runHistory).getByText(/Row 41/i)).toBeInTheDocument();

    await user.click(screen.getByLabelText(/enable scheduled execution/i));
    await user.click(screen.getByRole("button", { name: /publish batch definition/i }));

    expect(await screen.findByText(/workflow-item-/i)).toBeInTheDocument();
    expect(screen.getByText(/manual, scheduled/i)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /publish process service/i }));

    expect(await screen.findByText(/\/ogc\/processes\/processes\//i)).toBeInTheDocument();
    expect(screen.getByText(/process:invoke/i)).toBeInTheDocument();
  });

  it("validates syntactically valid non-workflow JSON without rendering graph nodes", async () => {
    const user = userEvent.setup();
    render(<StudioWorkflowEditor transport={createStudioWorkflowFixtureClient()} />);

    fireEvent.change(screen.getByLabelText(/workflow definition json/i), { target: { value: "{}" } });

    expect(screen.getByText(/Run validation to inspect contract issues before nodes render/i)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /^validate$/i }));

    expect(await screen.findByText(/^blocked$/i)).toBeInTheDocument();
    expect(screen.getByText(/Workflow definition must declare workflowId as a string/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /publish batch definition/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /publish process service/i })).toBeDisabled();
  });
});
