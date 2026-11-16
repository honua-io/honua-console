using Honua.Console.Contracts;
using System.Text.Json.Serialization;

namespace Honua.Console.Shell.Services;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ProposalRealtimeWire))]
[JsonSerializable(typeof(DeployOperationTransitionWire))]
internal sealed partial class ConsoleAdminLiveJsonContext : JsonSerializerContext;
