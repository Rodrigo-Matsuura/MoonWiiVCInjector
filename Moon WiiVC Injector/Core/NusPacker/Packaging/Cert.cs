// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)

using System;

namespace Moon_WiiVC_Injector.Core.NusPacker.Packaging;

public static class Cert
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

    public static byte[] GetCertAsData()
    {
        return (byte[])StaticCertData.Clone();
    }
}
