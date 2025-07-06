using Microsoft.Extensions.DependencyInjection;
using Sinos.Factories;
using Sinos.Services;

namespace Sinos.Compatibles;

public static class DependencyInjectionUtils
{
    /// <summary>
    /// 共通プロジェクトのサービスを登録する
    /// </summary>
    /// <typeparam name="T">ServiceCollectionの型</typeparam>
    /// <param name="serviceCollection">ServiceCollection</param>
    /// <returns></returns>
    public static T RegisterCommonServices<T>(this T serviceCollection) where T : IServiceCollection
    {
        serviceCollection
            // Services
            .AddSingleton<NeutrinoV1Service>()
            .AddSingleton<NeutrinoV2Service>()
            .AddSingleton<ProjectService>()
            .AddSingleton<ProjectSession>()
            .AddSingleton<SettingService>()
            // Factories
            .AddSingleton<ProjectSessionFactory>();

        return serviceCollection;
    }
}
