// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)

using System;
using System.IO;
using System.Threading;
using Moon_WiiVC_Injector.Core.NusPacker.Contents;
using Moon_WiiVC_Injector.Core.NusPacker.Crypto;
using Moon_WiiVC_Injector.Core.NusPacker.Utils;

namespace Moon_WiiVC_Injector.Core.NusPacker.Packaging;

public class NUSpackage
{
    private readonly FST fst;
    private readonly Ticket ticket;
    private readonly TMD tmd;
    private readonly string tempDir;

    public NUSpackage(FST fst, Ticket ticket, TMD tmd, string tempDir)
    {
        this.fst = fst;
        this.ticket = ticket;
        this.tmd = tmd;
        this.tempDir = tempDir;
    }

    public void PackContents(string outputDir, Action<string, double>? onProgress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDir);
        cancellationToken.ThrowIfCancellationRequested();

        using (Encryption encryption = GetEncryption())
        {
            fst.contents.PackContents(outputDir, encryption, tempDir, onProgress, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            onProgress?.Invoke("Packing FST into 00000000.app...", 0.9);

            string fstPath = Path.Combine(outputDir, "00000000.app");
            encryption.EncryptFileWithPadding(fst, fstPath, 0, Content.CONTENT_FILE_PADDING);

            Content fstContent = fst.contents.fstContent;
            fstContent.SHA1 = HashUtil.HashSHA1(fst.GetAsData());
            fstContent.encryptedFileSize = fst.GetDataSize();

            tmd.contentInfo.SHA2Hash = HashUtil.HashSHA2(fst.contents.GetAsData());
            tmd.UpdateContentInfoHash();

            cancellationToken.ThrowIfCancellationRequested();
            onProgress?.Invoke("Writing title metadata (title.tmd)...", 0.95);
            using (var fos = new FileStream(Path.Combine(outputDir, "title.tmd"), FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] tmdData = tmd.GetAsData();
                fos.Write(tmdData, 0, tmdData.Length);
            }

            onProgress?.Invoke("Writing certificate (title.cert)...", 0.97);
            using (var fos = new FileStream(Path.Combine(outputDir, "title.cert"), FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] certData = Cert.GetCertAsData();
                fos.Write(certData, 0, certData.Length);
            }

            onProgress?.Invoke("Writing ticket (title.tik)...", 0.99);
            using (var fos = new FileStream(Path.Combine(outputDir, "title.tik"), FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] tikData = ticket.GetAsData();
                fos.Write(tikData, 0, tikData.Length);
            }
        }
    }

    private Encryption GetEncryption()
    {
        return tmd.GetEncryption();
    }
}
