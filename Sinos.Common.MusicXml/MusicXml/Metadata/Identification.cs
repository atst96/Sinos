using System.Xml.Serialization;

namespace MusicXml.Metadata;

public class Identification
{
    [XmlElement("encoding")]
    public ScoreEncoding? Encoding { get; set; }

    public override string ToString()
        => $"Encoding={this.Encoding}";
}
