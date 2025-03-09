using System.Xml.Serialization;

namespace MusicXml;

public class ScorePartElement
{
    [XmlAttribute("id")]
    public string? Id { get; set; }

    [XmlElement("part-name")]
    public string? PartName { get; set; }

    public override string ToString()
        => $"Id={this.Id}, PartName={this.PartName}";
}
