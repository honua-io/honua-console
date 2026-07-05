using System.Globalization;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Test/demo-only SensorThings client backed by an in-memory STA catalog. Mirrors
/// the conformance-shaped read surface (Things/Datastreams/Observations,
/// <c>$expand</c> navigation, chart-ready time series) so render/integration
/// tests can exercise the browse + chart views without a live server. NEVER the
/// DI default — opt-in for tests, exactly like the other InMemory* clients.
/// </summary>
public sealed class InMemoryConsoleSensorThingsClient : IConsoleSensorThingsClient
{
    private readonly IReadOnlyList<StaThing> _things;
    private readonly IReadOnlyList<StaDatastream> _datastreams;
    private readonly IReadOnlyDictionary<long, IReadOnlyList<StaObservation>> _observations;

    public InMemoryConsoleSensorThingsClient()
        : this(CreateSeed())
    {
    }

    public InMemoryConsoleSensorThingsClient(SensorThingsSeed seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        _things = seed.Things;
        _datastreams = seed.Datastreams;
        _observations = seed.Observations;
    }

    public Task<SensorThingsReadResult<StaEntitySet<StaThing>>> ListThingsAsync(
        int top = 50,
        int skip = 0,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(SensorThingsReadResult<StaEntitySet<StaThing>>.Ok(
            Page(_things, top, skip)));

    public Task<SensorThingsReadResult<StaEntitySet<StaDatastream>>> ListDatastreamsAsync(
        int top = 50,
        int skip = 0,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(SensorThingsReadResult<StaEntitySet<StaDatastream>>.Ok(
            Page(_datastreams, top, skip)));

    public Task<SensorThingsReadResult<StaDatastream>> GetDatastreamAsync(
        long datastreamId,
        bool expandObservations = false,
        int observationTop = 200,
        CancellationToken cancellationToken = default)
    {
        var datastream = _datastreams.FirstOrDefault(d => d.IotId == datastreamId);
        if (datastream is null)
        {
            return Task.FromResult(SensorThingsReadResult<StaDatastream>.Denied(
                SensorThingsReadStatus.Missing,
                $"Datastream({datastreamId.ToString(CultureInfo.InvariantCulture)}) not found."));
        }

        if (expandObservations)
        {
            datastream = datastream with
            {
                Observations = ObservationsFor(datastreamId).Take(observationTop).ToList()
            };
        }

        return Task.FromResult(SensorThingsReadResult<StaDatastream>.Ok(datastream));
    }

    public Task<SensorThingsReadResult<StaEntitySet<StaObservation>>> ListDatastreamObservationsAsync(
        long datastreamId,
        int top = 200,
        int skip = 0,
        CancellationToken cancellationToken = default)
    {
        if (_datastreams.All(d => d.IotId != datastreamId))
        {
            return Task.FromResult(SensorThingsReadResult<StaEntitySet<StaObservation>>.Denied(
                SensorThingsReadStatus.Missing,
                $"Datastream({datastreamId.ToString(CultureInfo.InvariantCulture)}) not found."));
        }

        return Task.FromResult(SensorThingsReadResult<StaEntitySet<StaObservation>>.Ok(
            Page(ObservationsFor(datastreamId), top, skip)));
    }

    public Task<SensorThingsReadResult<SensorThingsTimeSeries>> GetDatastreamTimeSeriesAsync(
        long datastreamId,
        int top = 500,
        CancellationToken cancellationToken = default)
    {
        var datastream = _datastreams.FirstOrDefault(d => d.IotId == datastreamId);
        if (datastream is null)
        {
            return Task.FromResult(SensorThingsReadResult<SensorThingsTimeSeries>.Denied(
                SensorThingsReadStatus.Missing,
                $"Datastream({datastreamId.ToString(CultureInfo.InvariantCulture)}) not found."));
        }

        var points = ObservationsFor(datastreamId)
            .Take(top)
            .Select(o => new SensorThingsTimeSeriesPoint(
                DateTimeOffset.Parse(o.PhenomenonTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                o.Result))
            .OrderBy(p => p.PhenomenonTime)
            .ToList();

        return Task.FromResult(SensorThingsReadResult<SensorThingsTimeSeries>.Ok(
            new SensorThingsTimeSeries(
                datastreamId,
                datastream.Name,
                datastream.UnitOfMeasurement?.Symbol ?? string.Empty,
                points)));
    }

    private IReadOnlyList<StaObservation> ObservationsFor(long datastreamId) =>
        _observations.TryGetValue(datastreamId, out var observations) ? observations : [];

    private static StaEntitySet<T> Page<T>(IReadOnlyList<T> source, int top, int skip)
    {
        var clampedSkip = Math.Max(0, skip);
        var clampedTop = Math.Clamp(top, 1, 1000);
        var value = source.Skip(clampedSkip).Take(clampedTop).ToList();
        return new StaEntitySet<T> { Value = value, Count = source.Count };
    }

    /// <summary>
    /// Seeds a single air-temperature datastream (id=1) with an hourly 48-point
    /// series, matching the synthetic seed shipped by honua-server #1842 so tests
    /// can assert against the same demo datastream id.
    /// </summary>
    public static SensorThingsSeed CreateSeed()
    {
        var thing = new StaThing
        {
            IotId = 1,
            IotSelfLink = "https://example/sta/v1.1/Things(1)",
            Name = "Demo weather station",
            Description = "Synthetic SensorThings demo station.",
            DatastreamsNavigationLink = "https://example/sta/v1.1/Things(1)/Datastreams"
        };

        var datastream = new StaDatastream
        {
            IotId = 1,
            IotSelfLink = "https://example/sta/v1.1/Datastreams(1)",
            Name = "Air temperature",
            Description = "Hourly air-temperature observations.",
            ObservationType = "http://www.opengis.net/def/observationType/OGC-OM/2.0/OM_Measurement",
            UnitOfMeasurement = new StaUnitOfMeasurement
            {
                Name = "degree Celsius",
                Symbol = "°C",
                Definition = "http://unitsofmeasure.org/ucum.html#para-30"
            },
            PhenomenonTime = "2026-06-18T00:00:00Z/2026-06-19T23:00:00Z",
            ObservationsNavigationLink = "https://example/sta/v1.1/Datastreams(1)/Observations",
            ThingNavigationLink = "https://example/sta/v1.1/Datastreams(1)/Thing",
            Thing = thing
        };

        var observations = new List<StaObservation>(48);
        var start = new DateTimeOffset(2026, 6, 18, 0, 0, 0, TimeSpan.Zero);
        for (var hour = 0; hour < 48; hour++)
        {
            var time = start.AddHours(hour);
            var result = 15 + 8 * Math.Sin(hour / 24.0 * 2 * Math.PI);
            observations.Add(new StaObservation
            {
                IotId = hour + 1,
                IotSelfLink = $"https://example/sta/v1.1/Observations({(hour + 1).ToString(CultureInfo.InvariantCulture)})",
                PhenomenonTime = time.ToString("o", CultureInfo.InvariantCulture),
                Result = Math.Round(result, 2),
                DatastreamNavigationLink = "https://example/sta/v1.1/Observations(1)/Datastream"
            });
        }

        return new SensorThingsSeed(
            [thing],
            [datastream],
            new Dictionary<long, IReadOnlyList<StaObservation>> { [1] = observations });
    }
}

/// <summary>Seed payload for <see cref="InMemoryConsoleSensorThingsClient"/>.</summary>
public sealed record SensorThingsSeed(
    IReadOnlyList<StaThing> Things,
    IReadOnlyList<StaDatastream> Datastreams,
    IReadOnlyDictionary<long, IReadOnlyList<StaObservation>> Observations);
