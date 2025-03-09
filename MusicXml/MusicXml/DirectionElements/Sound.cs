using System.Xml.Serialization;

namespace MusicXml.DirectionElements;

public class Sound
{
    [XmlAttribute("tempo")]
    public double Tempo { get; set; }

    public override string ToString()
        => $"Tempo={this.Tempo}";
}
