using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Security;

public sealed class StoreClientCertificateResolver : IClientCertificateResolver
{
    private readonly INativeSecretStore _secrets;

    public StoreClientCertificateResolver(INativeSecretStore secrets)
    {
        _secrets = secrets;
    }

    public async ValueTask<X509Certificate2?> ResolveAsync(
        ConsoleEnvironmentProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();

        var reference = profile.ClientCertificate.Reference;
        if (!profile.ClientCertificate.Enabled ||
            reference is null ||
            reference.Kind == ConsoleClientCertificateReferenceKind.None)
        {
            return null;
        }

        return reference.Kind switch
        {
            ConsoleClientCertificateReferenceKind.FilePath => await LoadFromFileAsync(reference, cancellationToken)
                .ConfigureAwait(false),
            ConsoleClientCertificateReferenceKind.StoreThumbprint => FindInStore(
                reference,
                X509FindType.FindByThumbprint),
            ConsoleClientCertificateReferenceKind.StoreSubject => FindInStore(
                reference,
                X509FindType.FindBySubjectDistinguishedName),
            _ => null
        };
    }

    private async ValueTask<X509Certificate2?> LoadFromFileAsync(
        ConsoleClientCertificateReference reference,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reference.Value))
        {
            return null;
        }

        var password = string.IsNullOrWhiteSpace(reference.SecretName)
            ? null
            : await _secrets.GetSecretAsync(reference.SecretName, cancellationToken).ConfigureAwait(false);

        try
        {
            return X509CertificateLoader.LoadPkcs12FromFile(reference.Value, password);
        }
        catch (Exception ex) when (IsExpectedCertificateReferenceFailure(ex))
        {
            return null;
        }
    }

    private static bool IsExpectedCertificateReferenceFailure(Exception exception) =>
        exception is FileNotFoundException
            or DirectoryNotFoundException
            or IOException
            or UnauthorizedAccessException
            or CryptographicException;

    private static X509Certificate2? FindInStore(
        ConsoleClientCertificateReference reference,
        X509FindType findType)
    {
        if (string.IsNullOrWhiteSpace(reference.Value))
        {
            return null;
        }

        var storeName = Enum.TryParse<StoreName>(reference.StoreName, ignoreCase: true, out var parsedStoreName)
            ? parsedStoreName
            : StoreName.My;
        var storeLocation = Enum.TryParse<StoreLocation>(reference.StoreLocation, ignoreCase: true, out var parsedLocation)
            ? parsedLocation
            : StoreLocation.CurrentUser;

        using var store = new X509Store(storeName, storeLocation);
        store.Open(OpenFlags.ReadOnly);
        var certificates = store.Certificates.Find(findType, reference.Value, validOnly: false);
        return certificates.Count == 0 ? null : certificates[0];
    }
}
