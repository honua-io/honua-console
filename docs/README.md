---
type: index
title: "Honua Console documentation"
description: "What is documented here and what deliberately is not: Console's published surfaces for operators and integrators, with the design and planning corpus kept separate."
tags: [console, navigation]
---
# Honua Console documentation

Honua Console is the browser-deployable Blazor application for operating a Honua
server. This directory documents the surfaces that people **outside this
repository** depend on.

## Running it

[Run Console locally and in staging](deployment/LOCAL_AND_STAGING.md) covers
standing it up against a server, and what differs between the two environments.

[How Console authenticates operators](console-authentication.md) explains how an
operator signs in and what identity Console forwards to honua-server — worth
reading before you put it in front of anyone.

## Integrating with it

[Embedding contract](operate/embed-contract.md) states what another application
may rely on when it embeds Console surfaces, and what is explicitly outside that
promise.

[Shared Razor component API](reference/SHARED_COMPONENT_API.md) documents which
components the shared library exposes and which are internal to the Console
shell.

[Build artifact contract](deployment/BUILD_ARTIFACT.md) is the contract between
this repository and the single deployable artifact honua-devops produces.

## Running it natively

[The optional native host](native/MAUI_BLAZOR_HOST.md) describes the .NET MAUI
Blazor shell. Console remains a browser application; the native host renders the
same Razor routes and adds profile, certificate and gRPC wiring.

## What is not here

Most of `docs/` is working material rather than product documentation —
architecture information models, the design-to-engineering handoff corpus,
roadmap backlogs, the legacy-portal migration programme, and contributor
procedures. Those pages carry their own `Status:` lines saying as much, and they
are deliberately outside the published documentation bundle. The boundary and
the reason for every exclusion are recorded in
[`okf-bundle.v1.json`](okf-bundle.v1.json).

They join this index when the surfaces they describe ship.
