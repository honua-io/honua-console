using Honua.Console.Native.Core.Connections;
using Honua.Console.Native.Core.Security;
using Honua.Console.Native.Core.Storage;
using Honua.Console.Native.Core.Streaming;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Honua.Console.Native.Core.DependencyInjection;

public static class HonuaConsoleNativeCoreServiceCollectionExtensions
{
    public static IServiceCollection AddHonuaConsoleNativeCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IConsoleProfileStorage, InMemoryConsoleProfileStorage>();
        services.TryAddSingleton<INativeSecretStore, InMemoryNativeSecretStore>();
        services.Replace(ServiceDescriptor.Singleton<IConsoleEnvironmentProfileStore, JsonConsoleEnvironmentProfileStore>());
        services.Replace(ServiceDescriptor.Singleton<IConsoleAccountSessionStore, JsonConsoleAccountSessionStore>());
        services.TryAddSingleton<IClientCertificateResolver, StoreClientCertificateResolver>();
        services.TryAddSingleton<IConsoleAccountTokenProvider, NativeSecretStoreAccountTokenProvider>();
        services.TryAddSingleton<NativeHonuaConnectionFactory>();
        services.TryAddSingleton<IConsoleNativeStreamingProof, NativeGrpcTelemetryStreamingProof>();

        return services;
    }
}
