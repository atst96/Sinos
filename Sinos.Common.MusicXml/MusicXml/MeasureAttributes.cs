using MusicXml.MeasureAttributeElements;
using System.Xml.Serialization;

namespace MusicXml;

public class MeasureAttributes
{
    [XmlElement("divisions")]
    public int? Divisions { get; set; }

    [XmlIgnore]
    public bool DivisionsSpecified => this.Divisions.HasValue;

    [XmlElement("key")]
    public Key? Key { get; set; }

    [XmlElement("time")]
    public Time? Time { get; set; }

    [XmlElement("clef")]
    public Clef? Clef { get; set; }

    public override string ToString()
        => $"Divisions={this.Divisions}, Key={this.Key}, Time={this.Time}, Clef={this.Clef}";
}
