// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)

namespace Moon_WiiVC_Injector.Core.NusPacker.Crypto;

public class IV
{
    private const int LENGTH = 0x10;
    public readonly byte[] iv = new byte[LENGTH];

    public IV(byte[] iv)
    {
        if (iv?.Length == LENGTH)
        {
            this.iv = iv;
        }
    }
}

