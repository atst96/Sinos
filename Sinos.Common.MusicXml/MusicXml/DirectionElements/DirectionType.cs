using System.Xml.Serialization;

namespace MusicXml.DirectionElements;

public class DirectionType
{
    [XmlElement("metronome")]
    public MetronomeType? Metronome { get; set; }
}
