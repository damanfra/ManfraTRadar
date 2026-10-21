using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ManfraTorrent.App.Services;
using ManfraTorrent.App.ViewModels;
using ManfraTorrent.App.Views;
using ManfraTorrent.Core.Services;

namespace ManfraTorrent.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var store = SettingsStore.Load();
            var engine = new TorrentEngineService(store);
            var vm = new MainWindowViewModel(store, engine);

            var window = new MainWindow { DataContext = vm };
            UiServices.Window = window;
            desktop.MainWindow = window;

            // Salva o estado e fecha as conexões ao sair. Roda fora da thread de UI para não travar.
            desktop.Exit += (_, _) =>
            {
                try
                {
                    Task.Run(() => engine.ShutdownAsync()).Wait(TimeSpan.FromSeconds(10));
                }
                catch
                {
                    // saída do app: nada a fazer
                }
            };

            window.Opened += async (_, _) => await vm.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
