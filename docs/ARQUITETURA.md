# Arquitetura e stack

## Stack completa

| Camada | Tecnologia | Versão | Observações |
|---|---|---|---|
| Linguagem | C# | `latest` (C# 14 com .NET 10) | `Nullable` e `ImplicitUsings` ligados em todos os projetos (`Directory.Build.props`) |
| Runtime | .NET | 10.0 (LTS) | `net10.0` em `Directory.Build.props` |
| UI | Avalonia | 11.3.* | Multiplataforma (Windows, Linux, macOS). Tema `FluentTheme`, variante escura |
| Fonte da UI | Avalonia.Fonts.Inter | 11.3.* | `.WithInterFont()` em `Program.cs` |
| MVVM | CommunityToolkit.Mvvm | 8.4.* | `[ObservableProperty]` e `[RelayCommand]` por source generator |
| Motor BitTorrent | MonoTorrent | 3.0.2 | DHT, PEX, LSD, UPnP/NAT-PMP, magnet, web seeds, criptografia, modo streaming |
| HTTP | `HttpClient` + `SocketsHttpHandler` | BCL | Dois clientes: `Http.Api` (segue redirect) e `Http.Raw` (não segue, para capturar redirect para `magnet:`) |
| JSON | System.Text.Json | BCL | `settings.json` e resposta do Internet Archive |
| XML | System.Xml.Linq | BCL | Feeds Torznab |
| Fontes de conteúdo | Internet Archive (API `advancedsearch.php`) e Torznab | — | Torznab = Jackett ou Prowlarr |

Pacotes NuGet: `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`, `CommunityToolkit.Mvvm` (projeto App) e `MonoTorrent` (projeto Core).

## Projetos

```
ManfraTorrent.sln
├── src/ManfraTorrent.Core      biblioteca sem UI (pode virar CLI, serviço ou outro front-end)
│   ├── Models/                 Category, CategoryOption, SearchResult, AppSettings, SourceConfig
│   ├── Providers/              ITorrentProvider, ProviderFactory, InternetArchiveProvider, TorznabProvider
│   └── Services/               TorrentEngineService, TorrentFetcher, SettingsStore, Http, Format
└── src/ManfraTorrent.App       executável Avalonia (AssemblyName = ManfraTRadar)
    ├── App.axaml               recursos de cor, estilos globais, DataTemplates VM → View
    ├── Program.cs              ponto de entrada
    ├── Services/UiServices     clipboard, seletores de arquivo/pasta, abrir URL/pasta
    ├── ViewModels/             MainWindow, Discover, SearchResult, Downloads, DownloadItem, Sources/SourceItem
    └── Views/                  MainWindow, DiscoverView, DownloadsView, SourcesView
```

Os nomes `ManfraTorrent.*` dos projetos são anteriores ao nome do app; o executável já sai como `ManfraTRadar`.

## Fluxos principais

### Descobrir ("em alta" e busca)

1. `DiscoverViewModel.RefreshAsync` cria um `ITorrentProvider` para cada fonte ativa (`ProviderFactory`).
2. Consulta todas em paralelo. Sem termo de busca, chama `GetTrendingAsync`; com termo, `SearchAsync`.
3. Junta os resultados em `SearchResult` e ordena pela opção escolhida. "Mais populares" usa `Score` = seeders, ou downloads na semana quando a fonte não tem dados do swarm.
4. Uma fonte que falha não derruba as outras; o erro aparece na linha de status.
5. Ao buscar com termo, a ordenação muda para **Relevância**: mantém a ordem que cada fonte devolveu e intercala
   as fontes (1º de cada, depois 2º de cada...). Ao voltar para "Em alta", volta para "Mais populares".

Busca no Internet Archive: primeiro exige **todas** as palavras no título (`title:(a AND b AND c)`), sem `sort`
(relevância do próprio Archive). Se não achar nada, tenta todas as palavras em qualquer campo. O Archive só tem
domínio público e acervo; lançamentos comerciais só aparecem pelas fontes Torznab.

| Fonte | "Em alta" | Link usado |
|---|---|---|
| Internet Archive | `sort=week desc` (downloads na última semana) dentro de coleções por categoria | `https://archive.org/download/{id}/{id}_archive.torrent` |
| Torznab | busca vazia (feed recente) ordenada por seeders | `magneturl`, `link` ou `enclosure` do item |

Mapeamento de categorias Torznab: Filmes 2000, Séries 5000, Software 4000, Jogos 1000 e 4050, Livros 7000, Música 3000.

### Adicionar download

`TorrentEngineService.AddAsync(input, sequential)` aceita:

- `magnet:` → `MagnetLink.Parse`
- `http(s)://` → `TorrentFetcher` baixa sem seguir redirects automaticamente. Jackett/Prowlarr costumam responder 302 para `magnet:`, e o `HttpClient` não segue esse esquema, então o fetcher trata o redirect manualmente.
- caminho local de `.torrent` → `Torrent.LoadAsync`

Se o infohash já estiver no motor, só retoma o existente. Quando o download vem do Descobrir, o título do resultado
fica salvo em `AppSettings.DisplayNames` (infohash → título) e é o nome mostrado em Downloads, porque o nome interno
do torrent às vezes é só um identificador (caso do Internet Archive). `sequential = true` usa `AddStreamingAsync`, o modo streaming do MonoTorrent, que baixa as peças em ordem.

### Persistência

- `settings.json`: `AppSettings` (pasta, porta, limites, fontes, lista de pausados). Gravação atômica com `.tmp` + `File.Move`.
- `engine.state`: `ClientEngine.SaveStateAsync`, que guarda torrents, caminhos, prioridades e modo streaming. Salvo a cada 60 s (timer da tela Downloads), após adicionar ou remover, e ao fechar o app.
- Na abertura: `ClientEngine.RestoreStateAsync` e retomada de tudo que não está na lista `PausedTorrents`.
- `cache/` do MonoTorrent: fast resume (evita verificar os arquivos de novo), metadados de magnets e nós da DHT.

### Atualização da tela Downloads

`DispatcherTimer` de 1 s → `DownloadsViewModel.Refresh()` sincroniza a lista com `engine.Managers` e chama `DownloadItemViewModel.Update()` em cada item. É polling, sem eventos do MonoTorrent, para não lidar com troca de thread.

## Regras do MonoTorrent que o código respeita

- `ClientEngine.RemoveAsync` exige o torrent em `Stopped`; o serviço chama `StopAsync` antes.
- "Pausar" na UI = `StopAsync` (fecha conexões); "Retomar" = `StartAsync`.
- `Progress` vale `NaN` antes dos metadados chegarem; a VM trata isso.
- Os métodos do motor trocam para a thread interna (`MainLoop`). Chamar a partir da UI com `await` é seguro; ao fechar, o shutdown roda em `Task.Run` para não travar a thread de UI.

## Visual

Tema escuro próprio sobre o Fluent, com tokens em `App.axaml`:

| Token | Cor | Uso |
|---|---|---|
| `BgBrush` | `#0E1116` | fundo |
| `SurfaceBrush` / `Surface2Brush` | `#171B22` / `#1E232C` | cartões |
| `AccentBrush` | `#6EA8FF` | ação principal, download |
| `SeedBrush` | `#5EE0A0` | seeders, concluído |
| `LeechBrush` | `#FFA15C` | leechers, upload |
| `MutedBrush` | `#9AA3AF` | textos secundários |

O protótipo original (mobile) está em `docs/prototipo-android/`.
