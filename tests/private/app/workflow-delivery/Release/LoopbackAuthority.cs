using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace WorkflowDelivery.Tests.Release;

// A disposable CA and a loopback leaf naming 127.0.0.1 and localhost; native clients such as
// UV's rustls reject a self-signed end entity. Nothing is installed in a certificate store.
internal sealed class LoopbackAuthority : IDisposable
{
    internal string Bundle { get; }
    internal X509Certificate2 Leaf { get; }

    internal LoopbackAuthority(string directory)
    {
        using var caKey = RSA.Create(2048);
        var caRequest = new CertificateRequest("CN=loopback-contract-ca", caKey,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false,
            0, true));
        caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign, true));
        caRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(
            caRequest.PublicKey, false));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using X509Certificate2 ca = caRequest.CreateSelfSigned(now.AddMinutes(-5),
            now.AddHours(1));
        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest("CN=127.0.0.1", leafKey,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        // Other-realm redirect rows target localhost; a forwarded request must complete TLS
        // and reach the registry, so the row can observe it.
        names.AddDnsName("localhost");
        leafRequest.CertificateExtensions.Add(names.Build());
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,
            false, 0, true));
        leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1")], false));
        leafRequest.CertificateExtensions.Add(
            X509AuthorityKeyIdentifierExtension.CreateFromCertificate(ca, true, false));
        using X509Certificate2 signed = leafRequest.Create(ca, now.AddMinutes(-5),
            now.AddHours(1), RandomNumberGenerator.GetBytes(16));
        using X509Certificate2 paired = signed.CopyWithPrivateKey(leafKey);
        Leaf = X509CertificateLoader.LoadPkcs12(paired.Export(X509ContentType.Pkcs12),
            null);
        Bundle = System.IO.Path.Combine(directory, "loopback-ca.pem");
        File.WriteAllText(Bundle, ca.ExportCertificatePem() + "\n");
    }

    public void Dispose() => Leaf.Dispose();
}
