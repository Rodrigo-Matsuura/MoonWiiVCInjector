// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)

using System;
using System.IO;
using System.Security.Cryptography;
using Moon_WiiVC_Injector.Core.NusPacker.Crypto;
using Moon_WiiVC_Injector.Core.NusPacker.FileSystem;
using Moon_WiiVC_Injector.Core.NusPacker.Packaging;
using Moon_WiiVC_Injector.Core.NusPacker.Utils;

namespace Moon_WiiVC_Injector.Core.NusPacker.Metadata;

internal static class TitleCert
{
    private static readonly byte[] StaticCertData = InitializeCertData();

    private static byte[] InitializeCertData()
    {
        byte[] buffer = new byte[0xA00];

        Convert.FromHexString("00010003").CopyTo(buffer.AsSpan(0x000));
        Convert.FromHexString("526F6F74000000000000000000000000").CopyTo(buffer.AsSpan(0x240));
        Convert.FromHexString("00000001434130303030303030330000").CopyTo(buffer.AsSpan(0x280));

        Convert.FromHexString("00010004").CopyTo(buffer.AsSpan(0x400));
        Convert.FromHexString("526F6F742D4341303030303030303300").CopyTo(buffer.AsSpan(0x540));
        Convert.FromHexString("00000001435030303030303030620000").CopyTo(buffer.AsSpan(0x580));

        Convert.FromHexString("00010004").CopyTo(buffer.AsSpan(0x700));
        Convert.FromHexString("526F6F742D4341303030303030303300").CopyTo(buffer.AsSpan(0x840));
        Convert.FromHexString("00000001585330303030303030630000").CopyTo(buffer.AsSpan(0x880));

        return buffer;
    }

    public static byte[] GetCertAsData() => (byte[])StaticCertData.Clone();
}

internal sealed class TitleTicket
{
    public readonly long TitleId;
    public readonly byte[] DecryptedKey;
    public readonly byte[] EncryptWithKey;

    public TitleTicket(long titleId, byte[] decryptedKey, byte[] encryptWithKey)
    {
        TitleId = titleId;
        DecryptedKey = decryptedKey;
        EncryptWithKey = encryptWithKey;
    }

    public byte[] GetAsData()
    {
        Random rdm = new Random();
        BigEndianMemoryStream buffer = new BigEndianMemoryStream(0x350);

        byte[] b1 = Convert.FromHexString("00010004");
        buffer.Write(b1, 0, b1.Length);

        byte[] randomData = new byte[0x100];
        rdm.NextBytes(randomData);
        buffer.Write(randomData, 0, randomData.Length);

        buffer.Seek(0x3C, SeekOrigin.Current);
        byte[] issuer = Convert.FromHexString("526F6F742D434130303030303030332D58533030303030303063000000000000");
        buffer.Write(issuer, 0, issuer.Length);

        buffer.Seek(0x5C, SeekOrigin.Current);
        byte[] b2 = Convert.FromHexString("010000");
        buffer.Write(b2, 0, b2.Length);

        byte[] encKeyBytes = GetEncryptedKey();
        buffer.Write(encKeyBytes, 0, encKeyBytes.Length);

        byte[] b3 = Convert.FromHexString("000005");
        buffer.Write(b3, 0, b3.Length);

        randomData = new byte[0x06];
        rdm.NextBytes(randomData);
        buffer.Write(randomData, 0, randomData.Length);

        buffer.Seek(0x04, SeekOrigin.Current);
        buffer.WriteBigEndian(TitleId);

        byte[] b4 = Convert.FromHexString("00000011000000000000000000000005");
        buffer.Write(b4, 0, b4.Length);

        buffer.Seek(0xB0, SeekOrigin.Current);
        byte[] b5 = Convert.FromHexString("00010014000000AC000000140001001400000000000000280000000100000084000000840003000000000000FFFFFF01");
        buffer.Write(b5, 0, b5.Length);

        buffer.Seek(0x7C, SeekOrigin.Current);
        buffer.SetLength(0x350);
        return buffer.ToArray();
    }

    public byte[] GetEncryptedKey()
    {
        BigEndianMemoryStream ivStream = new BigEndianMemoryStream(0x10);
        ivStream.WriteBigEndian(TitleId);
        using var encrypt = new NusEncryption(EncryptWithKey, ivStream.GetBuffer());
        return encrypt.Encrypt(DecryptedKey);
    }
}

internal sealed class TitleTmd
{
    private const int SignatureType = 0x00010004;
    private readonly byte[] _signature = new byte[0x100];
    private static readonly byte[] Issuer = Convert.FromHexString("526F6F742D434130303030303030332D435030303030303030620000000000000000000000000000000000000000000000000000000000000000000000000000");

    private const byte Version = 0x01;
    private const byte CACRLVersion = 0x00;
    private const byte SignerCRLVersion = 0x00;

    private readonly long _systemVersion;
    private const int TitleType = 0x000100;
    private readonly short _groupId;
    private readonly uint _appType;
    private const int AccessRights = 0x0000;
    private readonly short _titleVersion;
    private readonly short _contentCount;
    private const short BootIndex = 0x00;
    private byte[] _sha2 = new byte[32];

    public readonly NusContentInfo ContentInfo;
    private readonly NusContentsCollection _contents;
    private readonly TitleTicket _ticket;

    public TitleTmd(AppXmlInfo appInfo, FstTable fst, TitleTicket ticket)
    {
        _groupId = appInfo.GroupId;
        _systemVersion = appInfo.OsVersion;
        _appType = appInfo.AppType;
        _titleVersion = appInfo.TitleVersion;
        _ticket = ticket;
        _contents = fst.Contents;
        _contentCount = _contents.GetContentCount();
        ContentInfo = new NusContentInfo(_contentCount)
        {
            Sha2Hash = SHA256.HashData(_contents.GetAsData())
        };
    }

    public void UpdateContentInfoHash()
    {
        _sha2 = SHA256.HashData(ContentInfo.GetAsData());
    }

    public byte[] GetAsData()
    {
        BigEndianMemoryStream buffer = new BigEndianMemoryStream(GetDataSize());

        buffer.WriteBigEndian(SignatureType);
        buffer.Write(_signature, 0, _signature.Length);
        buffer.Seek(60, SeekOrigin.Current);
        buffer.Write(Issuer, 0, Issuer.Length);

        buffer.WriteByte(Version);
        buffer.WriteByte(CACRLVersion);
        buffer.WriteByte(SignerCRLVersion);
        buffer.Seek(1, SeekOrigin.Current);

        buffer.WriteBigEndian(_systemVersion);
        buffer.WriteBigEndian(_ticket.TitleId);
        buffer.WriteBigEndian(TitleType);
        buffer.WriteBigEndian(_groupId);
        buffer.WriteBigEndian(_appType);
        buffer.Seek(58, SeekOrigin.Current);
        buffer.WriteBigEndian(AccessRights);
        buffer.WriteBigEndian(_titleVersion);
        buffer.WriteBigEndian(_contentCount);
        buffer.WriteBigEndian(BootIndex);
        buffer.Seek(2, SeekOrigin.Current);

        buffer.Write(_sha2, 0, _sha2.Length);

        byte[] infoData = ContentInfo.GetAsData();
        buffer.Write(infoData, 0, infoData.Length);
        byte[] contentsData = _contents.GetAsData();
        buffer.Write(contentsData, 0, contentsData.Length);
        buffer.SetLength(GetDataSize());

        return buffer.ToArray();
    }

    private int GetDataSize()
    {
        const int staticSize = 0x204;
        const int contentInfoSize = 0x40 * NusContentInfo.StaticDataSize;
        int contentsSize = _contents.GetDataSize();
        return staticSize + contentInfoSize + contentsSize;
    }

    public NusEncryption GetEncryption()
    {
        BigEndianMemoryStream ivStream = new BigEndianMemoryStream(0x10);
        ivStream.WriteBigEndian(_ticket.TitleId);
        return new NusEncryption(_ticket.DecryptedKey, ivStream.GetBuffer());
    }
}

