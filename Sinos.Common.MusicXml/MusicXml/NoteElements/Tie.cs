using System.Xml.Serialization;

namespace MusicXml.NoteElements;

public class Tie
{
    [XmlAttribute("type")]
    public StartStop Type { get; set; }

    public override string ToString()
        => $"Type={this.Type}";
}
