// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)

using System;
using System.Xml;

namespace Moon_WiiVC_Injector.Core.NusPacker.Metadata;

internal sealed class AppXmlInfo
{
    public long OsVersion { get; set; }
    public long TitleId { get; set; }
    public short TitleVersion { get; set; }
    public uint AppType { get; set; }
    public short GroupId { get; set; }
}

internal static class AppXmlParser
{
    public static AppXmlInfo Parse(string xmlPath)
    {
        var document = new XmlDocument();
        document.Load(xmlPath);

        string? osVersionStr = document.SelectSingleNode("app/os_version")?.InnerText;
        string? titleIdStr = document.SelectSingleNode("app/title_id")?.InnerText;
        string? titleVersionStr = document.SelectSingleNode("app/title_version")?.InnerText;
        string? appTypeStr = document.SelectSingleNode("app/app_type")?.InnerText;
        string? groupIdStr = document.SelectSingleNode("app/group_id")?.InnerText;

        return new AppXmlInfo
        {
            OsVersion = !string.IsNullOrWhiteSpace(osVersionStr) ? Convert.ToInt64(osVersionStr, 16) : 0L,
            TitleId = !string.IsNullOrWhiteSpace(titleIdStr) ? Convert.ToInt64(titleIdStr, 16) : 0L,
            TitleVersion = !string.IsNullOrWhiteSpace(titleVersionStr) ? unchecked((short)Convert.ToUInt16(titleVersionStr, 16)) : (short)0,
            AppType = !string.IsNullOrWhiteSpace(appTypeStr) ? Convert.ToUInt32(appTypeStr, 16) : 0x80000000,
            GroupId = !string.IsNullOrWhiteSpace(groupIdStr) ? unchecked((short)Convert.ToInt32(groupIdStr, 16)) : (short)0
        };
    }
}

