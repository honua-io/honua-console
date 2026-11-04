namespace Honua.Console.Shell.Models;

// Console-side model for the XLSForm (ODK Collect) import surface (Studio "Form · Import XLSForm").
// The conversion engine is server-owned (Honua.Collect.Core XlsFormImporter); the wire contract is
// Honua.Console.Contracts.FormPackageShims (import-xlsform endpoint). Nothing here fabricates a form: an
// import outcome is either a server-produced document (mapped through StudioFormPackageMapper) or carries
// a capability/binding state — and the importer's diagnostics are surfaced, never dropped.
