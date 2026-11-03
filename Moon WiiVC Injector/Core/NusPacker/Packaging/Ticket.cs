// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)

using System;
using System.IO;
using Moon_WiiVC_Injector.Core.NusPacker.Crypto;
using Moon_WiiVC_Injector.Core.NusPacker.Utils;

namespace Moon_WiiVC_Injector.Core.NusPacker.Packaging;

public class Ticket
{
    public readonly long titleID;
    public readonly Key decryptedKey;
    public readonly Key encryptWith;

    public Ticket(long titleID, Key decryptedKey, Key encryptWith)
    {
        this.titleID = titleID;
        this.decryptedKey = decryptedKey;
        this.encryptWith = encryptWith;
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

        byte[] encKeyBytes = GetEncryptedKey().key;
        buffer.Write(encKeyBytes, 0, encKeyBytes.Length);

        byte[] b3 = Convert.FromHexString("000005");
        buffer.Write(b3, 0, b3.Length);

        randomData = new byte[0x06];
        rdm.NextBytes(randomData);
        buffer.Write(randomData, 0, randomData.Length);

        buffer.Seek(0x04, SeekOrigin.Current);
        buffer.WriteBigEndian(titleID);

        byte[] b4 = Convert.FromHexString("00000011000000000000000000000005");
        buffer.Write(b4, 0, b4.Length);

        buffer.Seek(0xB0, SeekOrigin.Current);
        byte[] b5 = Convert.FromHexString("00010014000000AC000000140001001400000000000000280000000100000084000000840003000000000000FFFFFF01");
        buffer.Write(b5, 0, b5.Length);

        buffer.Seek(0x7C, SeekOrigin.Current);

        return buffer.GetBuffer();
    }

    public Key GetEncryptedKey()
    {
        BigEndianMemoryStream ivStream = new BigEndianMemoryStream(0x10);
        ivStream.WriteBigEndian(titleID);
        using Encryption encrypt = new Encryption(encryptWith, new IV(ivStream.GetBuffer()));

        return new Key(encrypt.Encrypt(decryptedKey.key));
    }
}
