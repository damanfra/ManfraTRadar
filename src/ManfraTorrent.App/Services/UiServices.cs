using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ManfraTorrent.App.ViewModels;
using ManfraTorrent.App.Views;

namespace ManfraTorrent.App.Services;

/// <summary>Acesso simples a recursos da janela (área de transferência, seletores de arquivo, abrir links).</summary>
public static class UiServices
{
    public static Window? Window { get; set; }

    public static async Task CopyAsync(string text)
    {
        if (Window?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    /// <summary>Abre URL, arquivo ou pasta com o programa padrão do sistema (Explorer, xdg-open...).</summary>
    public static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch
        {
            // sem programa associado: ignora
        }
    }

    /// <summary>Abre a janela de legendas como diálogo e já começa a verificação local.</summary>
    public static async Task ShowSubtitlesAsync(SubtitleViewModel vm)
    {
        if (Window is null) return;
        var dialog = new SubtitleWindow { DataContext = vm };
        vm.CloseRequested += dialog.Close;
        dialog.Opened += async (_, _) => await vm.InitializeAsync();
        await dialog.ShowDialog(Window);
    }

    public static async Task<string?> PickTorrentFileAsync()
    {
        if (Window is null) return null;
        var files = await Window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir arquivo .torrent",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Arquivo torrent") { Patterns = ["*.torrent"] }],
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public static async Task<string?> PickFolderAsync(string title)
    {
        if (Window is null) return null;
        var folders = await Window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }
}
