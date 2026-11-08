using Honua.Console.Web.Auth;

namespace Honua.Console.IntegrationTests;

public sealed partial class ConsoleServerSessionIdentityBindingTests
{
    [Fact]
    public async Task HumanServingIdentityCheck_ChangedSubject_ClearsTheServingBearer()
    {
        await using var harness = await Harness.CreateAsync();
        await harness.SignInAsync();
        Assert.NotNull(await harness.Sessions.GetSessionAsync("env"));
        var profile = Profile("env");
        var partition = harness.Partitions.GetOrCreate("operator-a", profile);
        harness.Handler.SessionJson = InvalidSession("subject");

        var accepted = await harness.Binding.EnsureAsync(new ConsoleServerSessionIdentityRequest
        {
            Partition = partition,
            Profile = profile,
            OperatorKey = "operator-a",
            ProviderKey = "oidc"
        }, default);

        Assert.False(accepted);
        Assert.Null(await harness.Sessions.GetSessionAsync("env"));
        Assert.False(harness.Partitions.TryGet("operator-a", profile, out _));
    }
}
