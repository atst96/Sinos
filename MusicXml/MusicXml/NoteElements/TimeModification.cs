using System.Xml.Serialization;

namespace MusicXml.NoteElements;


public class TimeModification
{
    [XmlElement("actual-notes")]
    public int ActualNotes { get; set; }

    [XmlElement("normal-notes")]
    public int NormalNotes { get; set; }

    public override string ToString()
        => $"ActualNotes={this.ActualNotes}, NormalNotes={this.NormalNotes}";
}
