// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)

using Moon_WiiVC_Injector.Core.NusPacker.Utils;

namespace Moon_WiiVC_Injector.Core.NusPacker.Crypto;

public class Key
{
    private const int LENGTH = 0x10;
    public readonly byte[] key = new byte[LENGTH];

    public Key(byte[] key)
    {
        if (key?.Length == LENGTH)
        {
            this.key = key;
        }
    }

    public Key(string s) : this(NusUtils.HexStringToByteArray(s))
    {
    }

    public override string ToString()
    {
        return NusUtils.ByteArrayToHexString(key);
    }
}
