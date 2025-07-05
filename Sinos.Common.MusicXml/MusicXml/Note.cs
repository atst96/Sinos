using MusicXml.NoteElements;
using System.Xml.Serialization;

namespace MusicXml;

public class Note
{
    [XmlElement("rest")]
    public Rest? Rest { get; set; }

    [XmlElement("pitch")]
    public Pitch? Pitch { get; set; }

    [XmlElement("duration")]
    public int Duration { get; set; }

    [XmlElement("tie")]
    public List<Tie>? Tie { get; set; }

    [XmlElement("type")]
    public string? Type { get; set; }

    [XmlElement("time-modification")]
    public TimeModification TimeModification { get; set; }

    [XmlElement("stem")]
    public string? Stem { get; set; }

    [XmlElement("accidental")]
    public string? Accidental { get; set; }

    [XmlElement("notations")]
    public Notations? Notations { get; set; }

    [XmlElement("lyric")]
    public Lyric? Lyric { get; set; }

    public override string ToString()
        => $"Rest={this.Rest}, Pitch={this.Pitch}, Duration={this.Duration}, Tie={this.Tie}, Type={this.Type}, Stem={this.Stem}, Accidental={this.Accidental}, Notations={this.Notations}, Lyric={this.Lyric}";
}

public class Rest
{
    [XmlAttribute("measure")]
    public string? Measure { get; set; }
}
