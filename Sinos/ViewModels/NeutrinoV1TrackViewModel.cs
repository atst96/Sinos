using Sinos.Projects.Tracks;
using Sinos.Services;

namespace Sinos.ViewModels;

internal class NeutrinoV1TrackViewModel : NeutrinoTrackViewModelBase
{
    private readonly NeutrinoV1Service _service;

    public NeutrinoV1TrackViewModel(NeutrinoV1Service service, ProjectViewModel projectViewModel, NeutrinoV1Track track)
        : base(projectViewModel, track)
    {
        this._service = service;
    }
}
