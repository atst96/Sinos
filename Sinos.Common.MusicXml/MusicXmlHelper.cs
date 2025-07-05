using System.Text;
using System.Xml.Serialization;

namespace MusicXml;

/// <summary>
/// MusicXMLのヘルパクラス
/// </summary>
internal static class MusicXmlHelper
{
    /// <summary>XMLデータのエンコーディング(BOM無しUTF-8)</summary>
    public static readonly UTF8Encoding TextEncode = new(false);

    /// <summary>
    /// XMLシリアライザを取得する。
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static XmlSerializer GetXmlSerializer<T>()
        => new(typeof(T));
}
