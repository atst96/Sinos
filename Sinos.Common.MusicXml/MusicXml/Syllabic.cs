using System.Xml.Serialization;

namespace MusicXml;

public enum Syllabic
{
    Unknown = 0,

    [XmlEnum("begin")]
    Begin = 1,

    [XmlEnum("end")]
    End = 2,

    [XmlEnum("middle")]
    Middle = 3,

    [XmlEnum("single")]
    Single = 4,
}
