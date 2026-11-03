// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)

using System;
using System.IO;
using Moon_WiiVC_Injector.Core.NusPacker.Contents;
using Moon_WiiVC_Injector.Core.NusPacker.Crypto;
using Moon_WiiVC_Injector.Core.NusPacker.Utils;

namespace Moon_WiiVC_Injector.Core.NusPacker.Packaging;

public class TMD
{
    private const int signatureType = 0x00010004;
    private readonly byte[] signature = new byte[0x100];
    private static readonly byte[] issuer = Convert.FromHexString("526F6F742D434130303030303030332D435030303030303030620000000000000000000000000000000000000000000000000000000000000000000000000000");

    private const byte version = 0x01;
    private const byte CACRLVersion = 0x00;
    private const byte signerCRLVersion = 0x00;

    private readonly long systemVersion;

    private const int titleType = 0x000100;
    private readonly short groupID;
    private readonly uint appType;
    private const int accessRights = 0x0000;
    private readonly short titleVersion;
    private readonly short contentCount;
    private const short bootIndex = 0x00;
    private byte[] SHA2 = new byte[32];

    public readonly ContentInfo contentInfo;
    private readonly Contents.Contents contents;

    private readonly Ticket ticket;

    public TMD(AppXMLInfo appInfo, FST fst, Ticket ticket)
    {
        groupID = appInfo.groupID;
        systemVersion = appInfo.osVersion;
        appType = appInfo.appType;
        titleVersion = appInfo.titleVersion;
        this.ticket = ticket;
        contents = fst.contents;
        contentCount = contents.GetContentCount();
        contentInfo = new ContentInfo(contentCount)
        {
            SHA2Hash = HashUtil.HashSHA2(contents.GetAsData())
        };
    }

    public void UpdateContentInfoHash()
    {
        SHA2 = HashUtil.HashSHA2(contentInfo.GetAsData());
    }

    public byte[] GetAsData()
    {
        BigEndianMemoryStream buffer = new BigEndianMemoryStream(GetDataSize());

        buffer.WriteBigEndian(signatureType);
        buffer.Write(signature, 0, signature.Length);
        buffer.Seek(60, SeekOrigin.Current);
        buffer.Write(issuer, 0, issuer.Length);

        buffer.WriteByte(version);
        buffer.WriteByte(CACRLVersion);
        buffer.WriteByte(signerCRLVersion);
        buffer.Seek(1, SeekOrigin.Current);

        buffer.WriteBigEndian(systemVersion);
        buffer.WriteBigEndian(ticket.titleID);
        buffer.WriteBigEndian(titleType);
        buffer.WriteBigEndian(groupID);
        buffer.WriteBigEndian(appType);
        buffer.Seek(58, SeekOrigin.Current);
        buffer.WriteBigEndian(accessRights);
        buffer.WriteBigEndian(titleVersion);
        buffer.WriteBigEndian(contentCount);
        buffer.WriteBigEndian(bootIndex);
        buffer.Seek(2, SeekOrigin.Current);

        buffer.Write(SHA2, 0, SHA2.Length);

        byte[] infoData = contentInfo.GetAsData();
        buffer.Write(infoData, 0, infoData.Length);
        byte[] contentsData = contents.GetAsData();
        buffer.Write(contentsData, 0, contentsData.Length);

        return buffer.GetBuffer();
    }

    private int GetDataSize()
    {
        const int staticSize = 0x204;
        const int contentInfoSize = 0x40 * ContentInfo.staticDataSize;
        int contentsSize = contents.GetDataSize();

        return staticSize + contentInfoSize + contentsSize;
    }

    public Encryption GetEncryption()
    {
        BigEndianMemoryStream ivStream = new BigEndianMemoryStream(0x10);
        ivStream.WriteBigEndian(ticket.titleID);

        return new Encryption(ticket.decryptedKey, new IV(ivStream.GetBuffer()));
    }
}
