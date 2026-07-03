// D1 (#265): the honua-server Studio package lifecycle DTOs are now projected by the server-owned
// Honua.Sdk.Studio package (namespace Honua.Sdk.Studio.Packages). These global using aliases bring the
// SDK projections into the Honua.Console.Contracts namespace under the SAME simple names the Console
// DataSources/authoring shells already reference, so consuming the SDK types is a behavior-identical
// swap for the deleted StudioPackageShims DTO fork (the "5th capability fork"). Console-only types that
// have NO SDK equivalent (the draft LIST summaries, the non-throwing StudioEndpointResult/Issue result
// envelope, and the IStudioPackageLifecycleClient adapter) stay defined in StudioPackageShims.cs.
//
// StudioContentVersionListResponse keeps its Console name but resolves to the SDK's StudioContentVersionList
// (identical wire shape: itemId + versions[]).

// Enums (StudioPackageEnums.cs)
global using StudioPackageFamily = Honua.Sdk.Studio.Packages.StudioPackageFamily;
global using StudioPackageOperation = Honua.Sdk.Studio.Packages.StudioPackageOperation;
global using StudioPackageSupportLevel = Honua.Sdk.Studio.Packages.StudioPackageSupportLevel;
global using StudioPackagePersistenceMode = Honua.Sdk.Studio.Packages.StudioPackagePersistenceMode;
global using StudioPackageValidationStatus = Honua.Sdk.Studio.Packages.StudioPackageValidationStatus;
global using StudioPackageDiagnosticSeverity = Honua.Sdk.Studio.Packages.StudioPackageDiagnosticSeverity;
global using StudioPublicationRequestStatus = Honua.Sdk.Studio.Packages.StudioPublicationRequestStatus;
global using StudioRollbackPointer = Honua.Sdk.Studio.Packages.StudioRollbackPointer;

// Domain DTOs (StudioPackageModels.cs)
global using StudioPackageBinding = Honua.Sdk.Studio.Packages.StudioPackageBinding;
global using StudioPackageDependency = Honua.Sdk.Studio.Packages.StudioPackageDependency;
global using StudioProvenanceRef = Honua.Sdk.Studio.Packages.StudioProvenanceRef;
global using StudioPublicationIntent = Honua.Sdk.Studio.Packages.StudioPublicationIntent;
global using StudioValidationDiagnostic = Honua.Sdk.Studio.Packages.StudioValidationDiagnostic;
global using StudioValidationSummary = Honua.Sdk.Studio.Packages.StudioValidationSummary;
global using StudioPackageEnvelope = Honua.Sdk.Studio.Packages.StudioPackageEnvelope;
global using StudioPackageFamilyDescriptor = Honua.Sdk.Studio.Packages.StudioPackageFamilyDescriptor;
global using StudioPackageFamilyCapabilities = Honua.Sdk.Studio.Packages.StudioPackageFamilyCapabilities;
global using StudioPackageDraft = Honua.Sdk.Studio.Packages.StudioPackageDraft;
global using StudioPreviewPlan = Honua.Sdk.Studio.Packages.StudioPreviewPlan;
global using StudioContentVersion = Honua.Sdk.Studio.Packages.StudioContentVersion;
global using StudioContentVersionListResponse = Honua.Sdk.Studio.Packages.StudioContentVersionList;
global using StudioPublicationRequest = Honua.Sdk.Studio.Packages.StudioPublicationRequest;
global using StudioContentItemPointers = Honua.Sdk.Studio.Packages.StudioContentItemPointers;
global using StudioRollbackRequest = Honua.Sdk.Studio.Packages.StudioRollbackRequest;
global using StudioVersionComparison = Honua.Sdk.Studio.Packages.StudioVersionComparison;

// Request bodies (StudioApiModels.cs / StudioPackageRequests.cs)
global using CreateStudioPackageDraftRequest = Honua.Sdk.Studio.Packages.CreateStudioPackageDraftRequest;
global using UpdateStudioPackageDraftRequest = Honua.Sdk.Studio.Packages.UpdateStudioPackageDraftRequest;
global using SaveStudioContentVersionRequest = Honua.Sdk.Studio.Packages.SaveStudioContentVersionRequest;
global using CreateStudioPublicationRequest = Honua.Sdk.Studio.Packages.CreateStudioPublicationRequest;
global using CreateStudioRollbackRequest = Honua.Sdk.Studio.Packages.CreateStudioRollbackRequest;
