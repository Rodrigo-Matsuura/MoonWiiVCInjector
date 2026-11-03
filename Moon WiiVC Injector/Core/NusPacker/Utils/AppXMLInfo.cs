// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)

using System;

namespace Moon_WiiVC_Injector.Core.NusPacker.Utils;

public class AppXMLInfo
{
    public long osVersion { get; set; }
    public long titleID { get; set; }
    public short titleVersion { get; set; }
    public uint sdkVersion { get; set; }
    public uint appType { get; set; }
    public short groupID { get; set; }
    public byte[] osMask { get; set; } = new byte[32];
    public long commonID { get; set; }

    public override string ToString()
    {
        return $"AppXMLInfo [OSVersion={osVersion}, titleID={titleID}, titleVersion={titleVersion}, " +
               $"SDKVersion={sdkVersion}, appType={appType}, groupID={groupID}, OSMask={Convert.ToHexString(osMask)}, common_id={commonID}]";
    }
}
