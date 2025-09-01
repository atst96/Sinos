using NAudio.Wave;
using Sinos.Data.Projects.Tracks;
using Sinos.Projects.Tracks.Base;

namespace Sinos.Projects.Tracks;

internal class AudioFileTrack : AudioTrackBase, IOffsetSeekableTrack
{
    private WaveStream _waveStream;

    /// <summary>ファイルパス</summary>
    public string FilePath { get; }

    /// <summary>開始位置</summary>
    public TimeSpan Offset { get; set; } = TimeSpan.Zero;

    public AudioFileTrack(Project project, string trackName, string path)
        : base(project, trackName)
    {
        this.FilePath = path;
        this._waveStream = GetAudioStream(path);
    }

    public AudioFileTrack(Project project, AudioFileTrackConfig config)
        : base(project, config)
    {
        this.FilePath = config.FilePath;
        this._waveStream = GetAudioStream(config.FilePath);

        this.IsMute = config.IsMute;
        //this.IsSolo = config.IsSolo;
        this.Volume = config.Volume;
        this.Offset = config.Offset;
    }

    protected override WaveStream LoadAudioStream()
        => this._waveStream;

    private static WaveStream GetAudioStream(string path)
        // TODO: 現時点でWAVの読み込みのみだが、
        //       将来的には他のフォーマットも読み込めるようにしたい
        => new WaveFileReader(path);

    public override TrackBaseConfig GetConfig()
        => new AudioFileTrackConfig()
        {
            TrackId = this.TrackId,
            TrackName = this.TrackName,
            FilePath = this.FilePath,
            IsMute = this.IsMute,
            //IsSolo = this.IsSolo,
            Volume = this.Volume,
            Offset = this.Offset,
        };
}
