// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)

using Moon_WiiVC_Injector.Core.NusPacker.Utils;

namespace Moon_WiiVC_Injector.Core.NusPacker.Contents;

public class ContentInfo
{
    public const int staticDataSize = 0x24;
    private readonly short indexOffset;
    private readonly short contentCount;
    public byte[] SHA2Hash { get; set; } = new byte[32];

    public ContentInfo(short contentCount) : this(0, contentCount)
    {
    }

    private ContentInfo(short indexOffset, short contentCount)
    {
        this.indexOffset = indexOffset;
        this.contentCount = contentCount;
    }

    public byte[] GetAsData()
    {
        BigEndianMemoryStream buffer = new BigEndianMemoryStream(2304);
        buffer.WriteBigEndian(indexOffset);
        buffer.WriteBigEndian(contentCount);
        buffer.Write(SHA2Hash, 0, SHA2Hash.Length);

        return buffer.GetBuffer();
    }
}

