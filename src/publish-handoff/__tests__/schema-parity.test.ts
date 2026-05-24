import type { ValidateFunction } from "ajv";
/**
 * Schema parity tests.
 *
 * These tests guard the cross-repo contract: the ServiceContentItem the
 * publish-handoff slice emits must validate against
 * `schemas/content-item-v1.json`, including pattern and format constraints.
 */
import addFormats from "ajv-formats";
import Ajv2020 from "ajv/dist/2020.js";
import { describe, expect, it } from "vitest";
import contentItemV1 from "../../../schemas/content-item-v1.json";
import { FixturePublishHandoffClient } from "../client.js";
import { deterministicIdGenerator, deterministicNow, makePublishEvent } from "./fixtures.js";

function buildAjv(): Ajv2020 {
  const ajv = new Ajv2020({ allErrors: true, strict: false });
  addFormats(ajv);
  return ajv;
}

function expectValid(validate: ValidateFunction, value: unknown): void {
  const ok = validate(value);
  expect(validate.errors ?? []).toEqual([]);
  expect(ok).toBe(true);
}

function expectInvalid(validate: ValidateFunction, value: unknown): string {
  const ok = validate(value);
  expect(ok).toBe(false);
  const errors = JSON.stringify(validate.errors ?? []);
  expect(errors).not.toBe("[]");
  return errors;
}

describe("schema parity: ServiceContentItem", () => {
  const validateItem = buildAjv().compile(contentItemV1);

  it("a fresh service item validates against content-item-v1", async () => {
    const client = new FixturePublishHandoffClient({
      generateId: deterministicIdGenerator("svc"),
      now: deterministicNow(),
    });
    const item = await client.receive(makePublishEvent());
    expectValid(validateItem, item);
  });

  it("a re-published service item still validates", async () => {
    const client = new FixturePublishHandoffClient({
      generateId: deterministicIdGenerator("svc"),
      now: deterministicNow(),
    });
    await client.receive(makePublishEvent({ sourceServiceId: "s" }));
    const updated = await client.receive(
      makePublishEvent({
        sourceServiceId: "s",
        eventKind: "metadataUpdate",
        title: "Renamed",
      }),
    );
    expectValid(validateItem, updated);
  });

  it("a degraded service item validates and carries a sanitized statusDetail", async () => {
    const client = new FixturePublishHandoffClient({
      generateId: deterministicIdGenerator("svc"),
      now: deterministicNow(),
    });
    const item = await client.receive(
      makePublishEvent({
        status: "degraded",
        statusReason: "Slower than usual response times.",
      }),
    );
    expectValid(validateItem, item);
    expect(item.target.statusDetail).toBe("Slower than usual response times.");
  });

  it("rejects schema-invalid ids and relative service links", async () => {
    const client = new FixturePublishHandoffClient({
      generateId: deterministicIdGenerator("svc"),
      now: deterministicNow(),
    });
    const item = await client.receive(makePublishEvent());

    expect(expectInvalid(validateItem, { ...item, id: "svc-001" })).toMatch(/pattern/);
    expect(
      expectInvalid(validateItem, {
        ...item,
        target: { ...item.target, serviceUrl: "/relative/service" },
        endpoints: {
          ...item.endpoints,
          geoservices: item.endpoints.geoservices
            ? { ...item.endpoints.geoservices, accessURL: "javascript:alert(1)" }
            : null,
        },
      }),
    ).toMatch(/serviceUrl|accessURL/);
  });

  it("rejects type='service' items that lack the service contract (serviceUrl + serviceType + status)", async () => {
    const client = new FixturePublishHandoffClient({
      generateId: deterministicIdGenerator("svc"),
      now: deterministicNow(),
    });
    const item = await client.receive(makePublishEvent());
    const broken = {
      ...item,
      target: {
        type: "service",
        serviceName: "broken/service",
        kind: "feature",
        layerCount: 0,
      } as Record<string, unknown>,
    };
    const errors = expectInvalid(validateItem, broken);
    expect(errors).toMatch(/serviceUrl/);
    expect(errors).toMatch(/serviceType/);
    expect(errors).toMatch(/status/);
  });

  it("rejects an unknown service status (operator vocabulary must be mapped, not pass through)", async () => {
    const client = new FixturePublishHandoffClient({
      generateId: deterministicIdGenerator("svc"),
      now: deterministicNow(),
    });
    const item = await client.receive(makePublishEvent());
    const broken = {
      ...item,
      target: { ...item.target, status: "publishing" },
    };
    expect(expectInvalid(validateItem, broken)).toMatch(/enum/);
  });
});
