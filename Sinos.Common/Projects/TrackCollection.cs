using System.Collections.ObjectModel;
using System.Text;
using Sinos.Data.Projects.Tracks;
using MusicXml;
using Sinos.Models.Neutrino;
using Sinos.Projects.Tracks;
using Sinos.Services;
using Sinos.Utils;
using System.Diagnostics.CodeAnalysis;

namespace Sinos.Projects;

internal class TrackCollection : ObservableCollection<TrackBase>
{
    private Project _project;
    private ProjectSession _session;

    public TrackCollection(Project project, ProjectSession session)
    {
        this._project = project;
        this._session = session;
    }

    /// <summary>
    /// インポートするファイルのパス
    /// </summary>
    /// <param name="path">パス</param>
    /// <param name="trackName">トラック名</param>
    public NeutrinoV1Track ImportFromMusicXmlV1(Part part, string trackName, ModelInfo model)
    {
        var xml = MusicXmlUtil.ToXmlString(part, trackName);
        var newTrack = new NeutrinoV1Track(this._project, trackName, xml, model);

        this.Add(newTrack);

        return newTrack;
    }

    /// <summary>
    /// インポートするファイルのパス
    /// </summary>
    /// <param name="path">パス</param>
    /// <param name="trackName">トラック名</param>
    public NeutrinoV2Track ImportFromMusicXmlV2(Part part, string trackName, ModelInfo model)
    {
        var xml = MusicXmlUtil.ToXmlString(part, trackName);
        var newTrack = new NeutrinoV2Track(this._project, trackName, xml, model);

        this.Add(newTrack);

        return newTrack;
    }

    public IEnumerable<TrackBaseConfig> GetConfig()
        => this.Select(t => t.GetConfig());

    public void Load(IEnumerable<TrackBaseConfig> tracks)
    {
        var project = this._project;
        var session = this._session;

        foreach (var track in tracks)
        {
            switch (track)
            {
                case NeutrinoV1TrackConfig t:
                    this.Add(new NeutrinoV1Track(project, t, session.NeutrinoV1.GetModels()));
                    break;

                case NeutrinoV2TrackConfig t:
                    this.Add(new NeutrinoV2Track(project, t, session.NeutrinoV2.GetModels()));
                    break;

                case AudioFileTrackConfig t:
                    this.Add(new AudioFileTrack(project, t));
                    break;
            }
        }
    }

    /// <summary>
    /// オーディオトラックを取得する
    /// TODO: 将来的には複数トラック似た奥するため、本メソッドは廃止予定。
    /// </summary>
    /// <param name="track"></param>
    /// <returns></returns>
    public bool TryGetAudioFileTrack([NotNullWhen(true)] out AudioFileTrack? track)
    {
        track = this.OfType<AudioFileTrack>().SingleOrDefault();
        return track != null;
    }
}
