using System.Xml.Serialization;

namespace MusicXml.MeasureAttributeElements;

public class Time
{
    [XmlElement("beats")]
    public int Beats { get; set; }

    [XmlElement("beat-type")]
    public int BeatType { get; set; }

    public override string ToString()
        => $"Beats={this.Beats}, BeatType={this.BeatType}";
}
