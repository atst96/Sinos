using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sinos.Compatibles;
using Sinos.Factories;

namespace Sinos.Services;

/// <summary>
/// サービスロケータ
/// </summary>
public static class ServiceLocator
{
    /// <summary>ServiceProvider</summary>
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    /// <summary>
    /// 初期化する
    /// </summary>
    /// <param name="action"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public static void Initialize(Action<IServiceCollection>? action = null)
    {
        if (ServiceProvider != null)
            throw new InvalidOperationException("ServiceLocator is already initialized.");

        var serviceCollection = new ServiceCollection();

        // 共有プロジェクト(Sinos.Common)のDI登録
        serviceCollection.RegisterContext();

        // 任意プロジェクトのDI登録
        action?.Invoke(serviceCollection);

        // ServiceProivider設定
        ServiceProvider = serviceCollection.BuildServiceProvider();
    }

    /// <summary>サービスを取得する</summary>
    public static T GetService<T>() where T : class
        => ServiceProvider.GetService<T>() ?? throw new Exception($"Type {typeof(T)} not found.");


    /// <summary>
    /// 共有プロジェクト(Sinos.Common)のサービスを登録
    /// </summary>
    /// <param name="services"></param>
    private static T RegisterContext<T>(this T services) where T : IServiceCollection
    {
        services
            // Logger
            .AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.SetMinimumLevel(LogLevel.Trace);
                builder.AddDebug();
            })
            // Services
            .AddSingleton<NeutrinoCommonService>()
            .AddSingleton<NeutrinoV1Service>()
            .AddSingleton<NeutrinoV2Service>()
            .AddSingleton<ProjectService>()
            .AddSingleton<ProjectSession>()
            .AddSingleton<SettingService>()
            // Factories
            .AddSingleton<ProjectSessionFactory>();

        return services;
    }
}
