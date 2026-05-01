using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;

namespace Rimshot;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            try
            {
                var iconStream = AssetLoader.Open(new Uri("avares://Rimshot/Assets/icon.png"));
                desktop.MainWindow.Icon = new WindowIcon(iconStream);
            }
            catch { /* icon is optional */ }
        }

        base.OnFrameworkInitializationCompleted();
    }
}