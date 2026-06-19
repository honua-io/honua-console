using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Merged-build SensorThings client used when no honua-server base URL is
/// configured. Every read returns the missing-binding surface (never seeded
/// data), per the Console Patterns Charter.
/// </summary>
public sealed class UnsupportedConsoleSensorThingsClient : IConsoleSensorThingsClient
{
    public Task<SensorThingsReadResult<StaEntitySet<StaThing>>> ListThingsAsync(
        int top = 50,
        int skip = 0,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Denied<StaEntitySet<StaThing>>());

    public Task<SensorThingsReadResult<StaEntitySet<StaDatastream>>> ListDatastreamsAsync(
        int top = 50,
        int skip = 0,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Denied<StaEntitySet<StaDatastream>>());

    public Task<SensorThingsReadResult<StaDatastream>> GetDatastreamAsync(
        long datastreamId,
        bool expandObservations = false,
        int observationTop = 200,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Denied<StaDatastream>());

    public Task<SensorThingsReadResult<StaEntitySet<StaObservation>>> ListDatastreamObservationsAsync(
        long datastreamId,
        int top = 200,
        int skip = 0,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Denied<StaEntitySet<StaObservation>>());

    public Task<SensorThingsReadResult<SensorThingsTimeSeries>> GetDatastreamTimeSeriesAsync(
        long datastreamId,
        int top = 500,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Denied<SensorThingsTimeSeries>());

    private static SensorThingsReadResult<T> Denied<T>() =>
        SensorThingsReadResult<T>.Denied(
            SensorThingsReadStatus.Unavailable,
            SensorThingsPresentation.MissingBindingMessage);
}
