# CLAUDE.md — ManfraTRadar

Contexto para o Claude (e para qualquer pessoa) trabalhar neste repositório.

## O que é

Cliente BitTorrent desktop **para uso pessoal** do Daniel, com uma aba **Descobrir** que mostra o que está
em alta nas fontes configuradas (Internet Archive embutido + endpoints Torznab do Jackett/Prowlarr).
Alvo: **Windows primeiro, Linux depois**, com o mesmo código.

- Repositório: https://github.com/damanfra/ManfraTRadar
- Idioma da UI, mensagens e docs: **português (Brasil)**. Código (identificadores) em inglês.

## Estado atual (v0.1)

- **O código nunca foi compilado.** Foi escrito num ambiente sem acesso ao NuGet. As APIs foram conferidas
  no código-fonte do MonoTorrent 3.0.2 e do Avalonia 11.3, mas a primeira tarefa numa máquina nova é
  `dotnet build` e corrigir o que aparecer.
- Telas prontas: Descobrir, Downloads, Fontes e ajustes.
- O ambiente de desenvolvimento será outra máquina e outra rede: o passo a passo está em `docs/AMBIENTE.md`.

## Stack

| | |
|---|---|
| Runtime | .NET 10 (`net10.0`, definido em `Directory.Build.props`) |
| UI | Avalonia 11.3.* + FluentTheme (escuro) + Avalonia.Fonts.Inter |
| MVVM | CommunityToolkit.Mvvm 8.4.* (source generators) |
| Motor | MonoTorrent 3.0.2 |
| Fontes | Internet Archive (`advancedsearch.php`) + Torznab (Jackett/Prowlarr) |

Detalhes, fluxos e decisões: `docs/ARQUITETURA.md`.

## Comandos

```bash
dotnet restore
dotnet build
dotnet run --project src/ManfraTorrent.App
dotnet publish src/ManfraTorrent.App -c Release -r win-x64   --self-contained -p:PublishSingleFile=true -o publish/win-x64
dotnet publish src/ManfraTorrent.App -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o publish/linux-x64
```

Ainda não há projeto de testes.

## Estrutura

- `src/ManfraTorrent.Core`: sem dependência de UI. Modelos, provedores de busca, motor, configurações.
- `src/ManfraTorrent.App`: Avalonia. `AssemblyName` = `ManfraTRadar`.
- `docs/`: ambiente, arquitetura e o protótipo mobile original (`prototipo-android/`).

Os nomes `ManfraTorrent.*` dos projetos/namespaces vêm de antes do nome final. Renomear para `ManfraTRadar.*`
é aceitável, desde que seja feito de uma vez (sln, csproj, namespaces, `x:Class` nos `.axaml`).

## Convenções

- ViewModels herdam `ViewModelBase` (`ObservableObject`) e usam `[ObservableProperty]` em campos privados
  camelCase e `[RelayCommand]` em métodos (o comando sai com sufixo `Command`; `FooAsync` vira `FooCommand`).
- **Bindings por reflexão**: `AvaloniaUseCompiledBindingsByDefault=false` no csproj. Os `.axaml` não usam `x:DataType`.
  Se migrar para compiled bindings, adicione `x:DataType` em todas as views e templates.
- Comandos de itens de lista usam `{Binding $parent[UserControl].DataContext.XxxCommand}` + `CommandParameter="{Binding}"`.
- Cores e estilos ficam só em `App.axaml` (tokens `*Brush` e classes como `card`, `primary`, `ghost`, `round`, `chips`, `rows`, `bar`, `muted`, `mono`).
  Não espalhe cores literais novas pelas views; crie um token.
- Ícones: `Path` com classe `icon` (traço, sem preenchimento), geometria no estilo 24×24.
- Formatação de tamanho, velocidade e tempo: sempre por `Core/Services/Format` (cultura pt-BR).
- Configuração nova do usuário: propriedade em `AppSettings` + campo em `SourcesViewModel` / `SourcesView`.
- Fonte de busca nova: implemente `ITorrentProvider`, adicione um valor em `SourceType` e trate em `ProviderFactory`.

## Cuidados com o MonoTorrent

- `RemoveAsync` exige o torrent parado (`StopAsync` antes).
- "Pausar" na UI = `StopAsync`. Os infohashes pausados ficam em `AppSettings.PausedTorrents`, para não serem retomados ao abrir.
- `TorrentManager.Progress` é `NaN` sem metadados.
- Estado salvo com `ClientEngine.SaveStateAsync` / `RestoreStateAsync` em `engine.state` na pasta de dados.
- Ao fechar o app, `ShutdownAsync` roda em `Task.Run` (no evento `Exit`) para não travar a thread de UI.
- Links de Jackett/Prowlarr podem redirecionar (302) para `magnet:`; por isso existe o `Http.Raw` sem auto-redirect e o `TorrentFetcher`.

## Pastas de dados

- Windows: `%APPDATA%\ManfraTRadar` — Linux: `~/.config/ManfraTRadar`
- Downloads padrão: `~/Downloads/ManfraTRadar`

## Próximos passos

1. Primeira compilação e correções.
2. **Tela de Configurações** (separada de "Fontes"), com:
   - velocidade máxima de upload e download (hoje fica em *Fontes e ajustes*; mover para cá);
   - ação ao terminar os downloads: nada, fechar o app ou desligar o PC
     (Windows: `shutdown /s /t 60`; Linux: `systemctl poweroff`; avisar com contagem regressiva e opção de cancelar);
   - adicionar e editar trackers: lista global somada a todo torrent novo, mais edição por torrent
     (`TorrentManager.TrackerManager.AddTrackerAsync` / `RemoveTrackerAsync`);
   - tema padrão: claro, escuro ou seguir o sistema (`Application.RequestedThemeVariant` = `Light`/`Dark`/`Default`).
     Exige mover as cores de `App.axaml` para `ResourceDictionary.ThemeDictionaries`, com a paleta clara do protótipo;
   - diretório de downloads padrão: definido ali e também atualizado sempre que o usuário escolher outra pasta
     em qualquer lugar do app (guarda o último usado);
   - bloquear conteúdo adulto (sim/não): descartar resultados das categorias Torznab 6000–6999 (XXX) e,
     como reforço, filtrar por palavras-chave no título. Vale para Descobrir e para a busca.
3. Escolher arquivos antes de baixar (`SetFilePriorityAsync(file, Priority.DoNotDownload)`).
4. Registrar o app como handler de `magnet:` (Windows: registro em `HKCU\Software\Classes\magnet`; Linux: `.desktop` com `MimeType=x-scheme-handler/magnet`) e aceitar o link via `args` em `Program.cs`.
5. Ícone na bandeja / iniciar minimizado.
6. Player embutido (LibVLCSharp) aproveitando o modo streaming.
7. Amarrar o tráfego a uma interface (VPN).
