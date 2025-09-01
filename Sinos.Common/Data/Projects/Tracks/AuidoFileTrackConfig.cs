using MemoryPack;

namespace Sinos.Data.Projects.Tracks;

[MemoryPackable(GenerateType.VersionTolerant)]
public partial class AudioFileTrackConfig : TrackBaseConfig
{
    private const int LatestVersion = 2;

    [MemoryPackOrder(2)]
    private int _version = LatestVersion;

    [MemoryPackOrder(3)]
    public required string FilePath { get; set; }

    [MemoryPackOrder(4)]
    public required bool IsMute { get; set; }

    [MemoryPackOrder(5)]
    public bool IsSolo { get; set; }

    [MemoryPackOrder(6)]
    public required float Volume { get; set; }

    [MemoryPackOrder(7)]
    public required TimeSpan Offset { get; set; } = TimeSpan.Zero;

    [MemoryPackOnDeserialized]
    private void Migrate()
    {
        if (this._version < 1)
        {
            this.IsMute = false;
            this.IsSolo = false;
            this.Volume = 1.0f;
        }

        if (this._version < 2)
        {
            this.Offset = TimeSpan.Zero;
        }
    }
}
