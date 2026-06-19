using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Reads the OGC SensorThings API (STA v1.1) browse surface from a connected
/// honua-server through the conformance-shaped <c>/sta/v1.1</c> contracts
/// (honua-server #1842 / #1747). The console uses this to list
/// Things/Datastreams/Observations, navigate <c>$expand=Observations</c>, and
/// chart a datastream's observations over time. Every read returns a
/// <see cref="SensorThingsReadResult{T}"/> whose status drives the shared
/// missing/forbidden/unsupported/unavailable surfaces; the server is the source
/// of truth and the surface is read-only (Phase 1).
/// </summary>
public interface IConsoleSensorThingsClient
{
    /// <summary>Lists the SensorThings <c>Things</c> (paged by <paramref name="top"/>).</summary>
    Task<SensorThingsReadResult<StaEntitySet<StaThing>>> ListThingsAsync(
        int top = 50,
        int skip = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the SensorThings <c>Datastreams</c> — the browseable catalog of
    /// observation series the console can chart.
    /// </summary>
    Task<SensorThingsReadResult<StaEntitySet<StaDatastream>>> ListDatastreamsAsync(
        int top = 50,
        int skip = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a single datastream by id, optionally expanding its observations
    /// inline (<c>$expand=Observations</c>) so a detail view can render the
    /// navigation in one read.
    /// </summary>
    Task<SensorThingsReadResult<StaDatastream>> GetDatastreamAsync(
        long datastreamId,
        bool expandObservations = false,
        int observationTop = 200,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists a datastream's observations via the
    /// <c>Datastreams(id)/Observations</c> navigation, ordered by phenomenon
    /// time so the result is chart-ready.
    /// </summary>
    Task<SensorThingsReadResult<StaEntitySet<StaObservation>>> ListDatastreamObservationsAsync(
        long datastreamId,
        int top = 200,
        int skip = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a datastream's observations and projects them into an ordered
    /// time series ready for the console's inline chart.
    /// </summary>
    Task<SensorThingsReadResult<SensorThingsTimeSeries>> GetDatastreamTimeSeriesAsync(
        long datastreamId,
        int top = 500,
        CancellationToken cancellationToken = default);
}
