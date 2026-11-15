using System.Net;
using Honua.Console.Contracts;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests;

public sealed class OptionalAlertAvailabilityTests
{
    [Theory]
    [InlineData(OperateAdminRoutes.Alerts + "?pageSize=50", true)]
    [InlineData(OperateAdminRoutes.AlertRules + "?serviceId=live", true)]
    [InlineData(OperateAdminRoutes.AlertZones + "?serviceId=live", true)]
    [InlineData(OperateAdminRoutes.AlertRules + "/42", false)]
    [InlineData(OperateAdminRoutes.AlertRules + "/42/health", false)]
    [InlineData("/api/v1/admin/jobs", false)]
    public void OnlyAbsentOptionalCollectionsReceiveSetupGuidance(string path, bool expected)
    {
        Assert.Equal(expected, OptionalAlertAvailability.IsAbsentCollection(HttpStatusCode.NotFound, path));
        Assert.False(OptionalAlertAvailability.IsAbsentCollection(HttpStatusCode.Forbidden, path));
        Assert.False(OptionalAlertAvailability.IsAbsentCollection(HttpStatusCode.InternalServerError, path));
    }
}
