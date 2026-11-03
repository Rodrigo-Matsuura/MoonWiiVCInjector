// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)

namespace Moon_WiiVC_Injector.Core.NusPacker.Packaging;

public class ContentDetails
{
    public readonly bool isHashed;
    public readonly short groupID;
    public readonly long parentTitleID;
    public readonly short entriesFlag;

    public ContentDetails(bool isHashed, short groupID, long parentTitleID, short entriesFlag)
    {
        this.isHashed = isHashed;
        this.groupID = groupID;
        this.parentTitleID = parentTitleID;
        this.entriesFlag = entriesFlag;
    }
}
