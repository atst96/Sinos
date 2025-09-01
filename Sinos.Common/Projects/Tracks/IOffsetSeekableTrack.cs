namespace Sinos.Projects.Tracks;

internal interface IOffsetSeekableTrack
{
    public TimeSpan Offset { get; set; }
}
