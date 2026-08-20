using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using YKWHome.App.ViewModels;
using YKWHome.App.Views;

namespace YKWHome.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Console.WriteLine("[app] XAML loaded");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainViewModel();
            vm.Log += msg => Console.WriteLine($"[app] {msg}");
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm,
            };
            Console.WriteLine("[app] MainWindow created");
        }

        base.OnFrameworkInitializationCompleted();
    }
}