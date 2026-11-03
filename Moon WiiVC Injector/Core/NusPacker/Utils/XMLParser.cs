// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)

using System;
using System.Xml;

namespace Moon_WiiVC_Injector.Core.NusPacker.Utils;

public class XMLParser
{
    private readonly XmlDocument document = new XmlDocument();

    public void LoadDocument(string path)
    {
        document.Load(path);
    }

    public AppXMLInfo GetAppXMLInfo()
    {
        AppXMLInfo appxmlinfo = new AppXMLInfo
        {
            osVersion = GetValueOfElementAsLongHex("app/os_version"),
            titleID = GetValueOfElementAsLongHex("app/title_id"),
            titleVersion = (short)GetValueOfElementAsLongHex("app/title_version"),
            sdkVersion = GetValueOfElementAsUnsignedInt("app/sdk_version"),
            appType = (uint)GetValueOfElementAsLongHex("app/app_type"),
            groupID = (short)GetValueOfElementAsLongHex("app/group_id"),
            osMask = GetValueOfElementAsByteArray("app/os_mask"),
            commonID = GetValueOfElementAsLongHex("app/common_id")
        };

        return appxmlinfo;
    }

    private uint GetValueOfElementAsUnsignedInt(string element)
    {
        return uint.Parse(GetValueOfElement(element) ?? "0");
    }

    private long GetValueOfElementAsLongHex(string element)
    {
        return Convert.ToInt64(GetValueOfElement(element) ?? "0", 16);
    }

    private byte[] GetValueOfElementAsByteArray(string element)
    {
        return NusUtils.HexStringToByteArray(GetValueOfElement(element) ?? "");
    }

    private string? GetValueOfElement(string element)
    {
        XmlNode? node = document.SelectSingleNode(element);
        if (node == null)
        {
            return null;
        }

        return node.InnerText;
    }
}
