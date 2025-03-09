using MusicXml.NotationElements;
using System.Xml.Serialization;

namespace MusicXml;

public class Notations
{
    [XmlElement("tied")]
    public Tied? Tied { get; set; }

    [XmlElement("articulations")]
    public Articulations? Articulations { get; set; }

    [XmlElement("slur")]
    public Slur? Slur { get; set; }

    [XmlElement("tuplet")]
    public Tuplet? Tuplet { get; set; }

    public override string ToString()
        => $"Tied={this.Tied}, Articulations={this.Articulations}, Slur={this.Slur}, Tuplet={this.Tuplet}";
}
