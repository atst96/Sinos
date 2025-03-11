using System.Xml;
using System.Xml.Serialization;

namespace MusicXml;

/// <summary>
/// MusicXMLのシリアライズクラス
/// </summary>
public class MusicXmlSerializer
{
    /// <summary>XMLシリアライズ設定</summary>
    private static readonly XmlWriterSettings _xmlWriterSetting = new()
    {
        NamespaceHandling = NamespaceHandling.OmitDuplicates,
        Encoding = MusicXmlHelper.TextEncode,
    };

    /// <summary>XMLシリアライズ時の名前空間(明示的に未設定)</summary>
    private static readonly XmlSerializerNamespaces _xmlWriterNamespaces = new([XmlQualifiedName.Empty]);

    /// <summary>
    /// オブジェクトをMusicXMLに変換する。
    /// </summary>
    /// <typeparam name="T">シリアライズ前の型情報</typeparam>
    /// <param name="obj">対象オブジェクト</param>
    /// <returns>シリアライズ後データ</returns>
    public static byte[] ToXmlByteArray<T>(T obj) where T : class
    {
        const string PubId = "-//Recordare//DTD MusicXML 4.0 Partwise//EN";
        const string SysId = "http://www.musicxml.org/dtds/partwise.dtd";

        using var ms = new MemoryStream();
        using var writer = XmlWriter.Create(ms, _xmlWriterSetting);
        writer.WriteDocType("score-partwise", PubId, SysId, null);

        MusicXmlHelper.GetXmlSerializer<T>().Serialize(writer, obj, _xmlWriterNamespaces);
        return ms.ToArray();
    }

    /// <summary>
    /// オブジェクトをMusicXMLに変換する。
    /// </summary>
    /// <typeparam name="T">シリアライズ前の型情報</typeparam>
    /// <param name="obj">対象オブジェクト</param>
    /// <returns>シリアライズ後データ</returns>
    public static string ToXmlString<T>(T obj) where T : class
        => MusicXmlHelper.TextEncode.GetString(ToXmlByteArray(obj));
}
