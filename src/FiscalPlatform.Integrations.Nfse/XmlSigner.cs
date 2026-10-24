using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using System.Xml.Linq;

namespace FiscalPlatform.Integrations.Nfse;

/// <summary>
/// Assinatura XMLDSig envelopada (padrão do Sistema Nacional NFS-e): referência ao Id do elemento
/// assinado (infDPS / infPedReg), transforms enveloped + C14N, certificado do emitente no KeyInfo.
/// A <c>Signature</c> é inserida como irmã do elemento assinado, ao final do elemento raiz.
/// </summary>
public static class XmlSigner
{
    public static XmlDocument Sign(XDocument document, string signedElementName, X509Certificate2 certificate, bool useSha1 = false)
    {
        var xml = new XmlDocument { PreserveWhitespace = true };
        using (var reader = document.CreateReader()) xml.Load(reader);
        if (xml.FirstChild is not XmlDeclaration && document.Declaration is { } decl)
            xml.InsertBefore(xml.CreateXmlDeclaration(decl.Version ?? "1.0", decl.Encoding ?? "UTF-8", null), xml.DocumentElement);

        var target = xml.GetElementsByTagName(signedElementName, DpsXmlBuilder.Ns.NamespaceName).OfType<XmlElement>().Single();
        var id = target.GetAttribute("Id");

        using var rsa = certificate.GetRSAPrivateKey()
            ?? throw new CryptographicException("O certificado não possui chave privada RSA utilizável.");
        var signed = new SignedXml(xml) { SigningKey = rsa };
        signed.SignedInfo!.CanonicalizationMethod = SignedXml.XmlDsigC14NTransformUrl;
        signed.SignedInfo.SignatureMethod = useSha1 ? SignedXml.XmlDsigRSASHA1Url : SignedXml.XmlDsigRSASHA256Url;

        var reference = new Reference("#" + id) { DigestMethod = useSha1 ? SignedXml.XmlDsigSHA1Url : SignedXml.XmlDsigSHA256Url };
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigC14NTransform());
        signed.AddReference(reference);

        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(certificate));
        signed.KeyInfo = keyInfo;

        signed.ComputeSignature();
        xml.DocumentElement!.AppendChild(xml.ImportNode(signed.GetXml(), true));
        return xml;
    }
}
