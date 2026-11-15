using System.Net;
using Honua.Console.Contracts;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests;

public sealed class OptionalAlertAvailabilityTests
{
    [Theory]
    [InlineData(OperateAdminRoutes.Alerts + "?pageSize=50")]
    [InlineData(OperateAdminRoutes.AlertRules + "?serviceId=live")]
    [InlineData(OperateAdminRoutes.AlertZones + "?serviceId=live")]
    public void OnlyAbsentOptionalCollectionsReceiveSetupGuidance(string path)
    {
        Assert.True(OptionalAlertAvailability.IsAbsentCollection(HttpStatusCode.NotFound, path));
        Assert.True(OptionalAlertAvailability.IsAbsentCollection(HttpStatusCode.NotImplemented, path));
        Assert.False(OptionalAlertAvailability.IsAbsentCollection(HttpStatusCode.Forbidden, path));
        Assert.False(OptionalAlertAvailability.IsAbsentCollection(HttpStatusCode.InternalServerError, path));
    }
    [Theory]
    [InlineData(OperateAdminRoutes.AlertRules + "/42")]
    [InlineData(OperateAdminRoutes.AlertRules + "/42/health")]
    [InlineData("/api/v1/admin/jobs")]
    public void MissingIndividualRecordsAndOtherCollectionsKeepTheirErrorState(string path)
    {
        Assert.False(OptionalAlertAvailability.IsAbsentCollection(HttpStatusCode.NotFound, path));
        Assert.False(OptionalAlertAvailability.IsAbsentCollection(HttpStatusCode.NotImplemented, path));
    }
}
