using System;
using System.IO;
using Sinos.Mvvm;
using Sinos.Projects.Tracks;

namespace Sinos.ViewModels;

internal class AudioTrackViewModel(AudioFileTrack track) : ViewModelBase
{
    /// <summary>Audio track</summary>
    private AudioFileTrack _track = track;

    /// <summary>ファイルパス</summary>
    public string FilePath { get; } = track.FilePath;

    /// <summary>ファイル名</summary>
    public string FileName { get; } = Path.GetFileName(track.FilePath);

    /// <summary>ボリューム</summary>
    public float Volume
    {
        get => this._track.Volume;
        set
        {
            var track = this._track;
            if (track.Volume != value)
            {
                track.Volume = value;
                this.OnPropertyChanged();
            }
        }
    }

    /// <summary>ミュート</summary>
    public bool IsMute
    {
        get => this._track.IsMute;
        set
        {
            var track = this._track;

            if (track.IsMute != value)
            {
                track.IsMute = value;
                this.OnPropertyChanged();
            }
        }
    }

    /// <summary>開始位置</summary>
    public double Offset
    {
        get => this._track.Offset.TotalSeconds;
        set
        {
            var track = this._track;
            if (track.Offset.TotalSeconds != value)
            {
                track.Offset = TimeSpan.FromSeconds(value);
                this.OnPropertyChanged();
            }
        }
    }
}
