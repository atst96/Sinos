using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Sinos.Views;
using Sinos.Services;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Sinos.ViewModels;

namespace Sinos;

public partial class App : Application
{
    /// <summary>アプリケーション名</summary>
    public const string AppName = "Sinos";

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // ServiceLocatorを初期化
        ServiceLocator.Initialize(services =>
        {
            services
                // Services
                .AddSingleton<AudioSessionManager>()
                .AddTransient<DialogService>()
                .AddSingleton<ProjectFactory>()
                .AddSingleton<ProjectManager>()
                .AddSingleton<ViewModelFactory>()
                // ViewModels
                .AddTransient<MainWindowViewModel>()
                .AddTransient<PreferenceWindowViewModel>()
                .AddTransient<ProgressWindowViewModel>()
            ;
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Line below is needed to remove Avalonia data validation.
        // Without this line you will get duplicate validations from both Avalonia and CT
        BindingPlugins.DataValidators.RemoveAt(0);

        switch (this.ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new MainWindow();
                break;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
