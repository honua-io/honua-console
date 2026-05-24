namespace Honua.Console.Shell.Models;

public static class ConsoleEnvironmentProfileDefaults
{
    public const string DevelopmentProfileId = "dev";
    public const string StagingProfileId = "staging";

    public static IReadOnlyList<ConsoleEnvironmentProfile> CreateProfiles() =>
    [
        new()
        {
            Id = DevelopmentProfileId,
            DisplayName = "Honua Dev",
            ServerBaseUri = new Uri("https://dev.honua.local"),
            EnvironmentKind = "development",
            TenantId = "honua-dev",
            TransportCapabilities = new ConsoleEnvironmentTransportCapabilities
            {
                BrowserHttp = true,
                BrowserRealtime = true,
                NativeGrpc = true,
                NativeMtls = false
            },
            Account = new ConsoleAccountBinding
            {
                AuthMode = ConsoleAccountAuthMode.AccountRbac,
                AccountId = "operator.dev",
                DisplayName = "Dev Operator",
                TenantId = "honua-dev",
                PermissionHints = ["catalog:read", "operate:jobs:read", "studio:drafts:write"]
            }
        },
        new()
        {
            Id = StagingProfileId,
            DisplayName = "Honua Staging",
            ServerBaseUri = new Uri("https://staging.honua.example"),
            EnvironmentKind = "staging",
            TenantId = "honua-staging",
            TransportCapabilities = new ConsoleEnvironmentTransportCapabilities
            {
                BrowserHttp = true,
                BrowserRealtime = true,
                NativeGrpc = true,
                NativeMtls = true
            },
            Account = new ConsoleAccountBinding
            {
                AuthMode = ConsoleAccountAuthMode.AccountRbac,
                AccountId = "operator.staging",
                DisplayName = "Staging Operator",
                TenantId = "honua-staging",
                PermissionHints = ["catalog:read", "operate:jobs:read", "operate:telemetry:read"]
            },
            ClientCertificate = new ConsoleClientCertificateBinding
            {
                Enabled = true,
                Reference = new ConsoleClientCertificateReference
                {
                    Kind = ConsoleClientCertificateReferenceKind.StoreSubject,
                    Value = "CN=Honua Operator Staging",
                    StoreName = "My",
                    StoreLocation = "CurrentUser"
                }
            }
        }
    ];

    public static IReadOnlyList<ConsoleEnvironmentState> CreateStates() =>
    [
        new()
        {
            ProfileId = DevelopmentProfileId,
            LastRoute = "/studio",
            LastStreamingResumeToken = "dev-resume-0"
        },
        new()
        {
            ProfileId = StagingProfileId,
            LastRoute = "/operate/native-stream",
            LastStreamingResumeToken = "staging-resume-0"
        }
    ];
}
