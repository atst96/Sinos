using Sinos.Projects;
using Sinos.Services;

namespace Sinos.Factories;

internal class ProjectSessionFactory
{
    private NeutrinoV1Service _v1Service;
    private NeutrinoV2Service _v2Service;
    private SettingService _settingService;

    public ProjectSessionFactory(NeutrinoV1Service v1Service, NeutrinoV2Service v2Service, SettingService settingService)
    {
        this._v1Service = v1Service;
        this._v2Service = v2Service;
        this._settingService = settingService;
    }

    public ProjectSession Create(Project project)
        => new(project, this._v1Service, this._v2Service, this._settingService);
}
