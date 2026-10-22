# CLAUDE.md — ManfraTRadar

Contexto para o Claude (e para qualquer pessoa) trabalhar neste repositório.

## O que é

Cliente BitTorrent desktop **para uso pessoal** do Daniel, com uma aba **Descobrir** que mostra o que está
em alta nas fontes configuradas (Internet Archive embutido + endpoints Torznab do Jackett/Prowlarr).
Alvo: **Windows primeiro, Linux depois**, com o mesmo código.

- Repositório: https://github.com/damanfra/ManfraTRadar
- Idioma da UI, mensagens e docs: **português (Brasil)**. Código (identificadores) em inglês.

## Estado atual (v0.1)

- **Compila e roda no Windows** (primeira execução em 22/10/2026, .NET SDK 10.0.402, sem erros nem avisos).
  Testado: Descobrir com o Internet Archive, busca, painel de detalhes e dois downloads do Archive até o fim
  (semeando). Fontes Torznab ainda não testadas com um Jackett/Prowlarr real. Linux ainda não testado.
- Pasta de desenvolvimento no PC do Daniel: `C:\Projetos\ManfraTRadar`.
- Telas prontas: Descobrir, Downloads, Fontes e ajustes.
- Para montar o ambiente numa máquina nova: `docs/AMBIENTE.md`.

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
- Versão do app: `<Version>` em `Directory.Build.props`; aparece na barra de título (`Services/AppInfo`).
  Ícone: `src/ManfraTorrent.App/Assets/icon.ico` (mesmo desenho dos três círculos da barra lateral; serve para janela e .exe).
- Formatação de tamanho, velocidade e tempo: sempre por `Core/Services/Format` (cultura pt-BR).
- Configuração nova do usuário: propriedade em `AppSettings` + campo em `SourcesViewModel` / `SourcesView`.
- Fonte de busca nova: implemente `ITorrentProvider`, adicione um valor em `SourceType` e trate em `ProviderFactory`.
- Filtros e agrupamento de resultados ficam só em `Core/Services/ResultFilter` (aplicado no `DiscoverViewModel`).
- Legendas: tudo em `Core/Subtitles` (ver `docs/LEGENDAS.md`). Regra do Daniel: **nunca buscar automaticamente**;
  só em download concluído, com confirmação, uma busca por serviço por arquivo e um download por escolha.
  Preferência do Daniel: só baixa filmes/séries FullHD ou melhor, com bitrate decente (daí o mínimo de 400 MB).

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

1. **PRÓXIMA TAREFA — Release automática + auto-atualização** (mesmo padrão de release dos outros apps do Daniel,
   ex.: Android-Streaming-Hub e Android-media-downloader):
   - **Workflow** `.github/workflows/release.yml`: dispara em push na `main` (com `paths-ignore` para `**.md` e `docs/**`)
     e por `workflow_dispatch`; `permissions: contents: write`; roda em `windows-latest` (o ícone do .exe só é
     embutido quando o build roda no Windows); `actions/setup-dotnet` 10.x; versão `0.1.${{ github.run_number }}`
     passada com `-p:Version=...`; publica `win-x64` e `linux-x64` self-contained single-file; empacota
     `ManfraTRadar-v0.1.N-win-x64.zip` e `ManfraTRadar-v0.1.N-linux-x64.tar.gz`; publica com
     `softprops/action-gh-release@v2` (tag `v0.1.N`, `make_latest: true`, corpo com o commit).
   - **Verificação no app** (`Core/Services/UpdateService`): consulta
     `https://api.github.com/repos/damanfra/ManfraTRadar/releases/latest` (repo público, sem token) ao abrir e a cada
     6 h; compara com `AppInfo.Version`; escolhe o asset do sistema atual. Build local (`dotnet run`, versão do
     `Directory.Build.props`) não oferece atualização.
   - **Aviso**: faixa discreta na barra lateral "Nova versão v0.1.N — Atualizar" + opção em Configurações
     ("Verificar atualizações automaticamente" e "Verificar agora").
   - **Aplicar (portátil, sem instalador)**: baixar o pacote para a pasta temporária, extrair; no Windows renomear o
     `ManfraTRadar.exe` em execução para `ManfraTRadar.old.exe` (o Windows permite renomear exe em uso), colocar o novo,
     salvar o estado do motor (`ShutdownAsync`), abrir o novo e sair; ao iniciar, apagar `*.old.exe`. No Linux,
     substituir o arquivo e reiniciar. Alternativa avaliada: Velopack (instalador + atalhos + delta), se um dia quiser instalador.
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
     como reforço, filtrar por palavras-chave no título. Vale para Descobrir e para a busca;
   - intervalo de atualização do Descobrir: nunca / 15 / 30 / 60 min (padrão 30).
3. **Atualização automática do Descobrir** (hoje só atualiza ao abrir o app, trocar categoria, buscar ou salvar fontes):
   - timer conforme o intervalo configurado; pular enquanto a janela estiver minimizada e atualizar ao voltar,
     se o intervalo já tiver passado;
   - não atrapalhar quem está olhando: manter o item selecionado e a posição da lista. Durante uma busca,
     mostrar o aviso "Há novidades — atualizar" em vez de trocar a lista sozinho;
   - mostrar "atualizado há N min" ao lado do título, com botão de atualizar;
   - guardar o último resultado em disco (pasta de dados) para a lista aparecer na hora ao abrir o app,
     atualizando em segundo plano.
4. **Legendas PT-BR sob demanda** — **implementado, falta testar com chaves reais** (detalhes e pendências em
   `docs/LEGENDAS.md`). Resumo: só para downloads **concluídos**,
   **nunca automático** (o usuário clica no item e confirma); verificação local primeiro (`.srt` já existente, faixa PT
   embutida no MKV, hash do OpenSubtitles); uma busca por serviço por arquivo; o usuário escolhe e só então baixa 1
   legenda; cache local de buscas e controle da cota diária do OpenSubtitles (download conta, busca não); SubDL como
   segunda fonte (limite diário não publicado, pacotes de temporada); grava em UTF-8 com terminação configurável (Plex).
   Pendente: vários episódios de uma vez; definir a terminação padrão depois do teste no Plex.
   **Planejado (sem data):** a busca também como mini-app separado (`ManfraTorrent.SubtitleApp` + biblioteca
   `ManfraTorrent.Subtitles`), para buscar vídeos que já saíram dos concluídos; o app principal continua chamando a
   mesma janela. Passos em `docs/LEGENDAS.md`, seção "Futuro: busca de legendas como mini-app separado".
5. **Controle remoto pelo Android** (o Android só controla; quem baixa é sempre o desktop):
   - **API no desktop:** servidor HTTP embutido no app (ASP.NET Core / Kestrel) com endpoints para buscar no Descobrir
     (categoria + termo, usando as fontes do desktop, inclusive Jackett/Prowlarr), adicionar download (link + "em ordem"),
     listar downloads com progresso e velocidades, e pausar, retomar ou remover. Liga/desliga e porta em Configurações;
   - **Pareamento por QR code:** o desktop mostra endereço + chave de acesso; toda chamada exige a chave. Só rede local por
     padrão; fora de casa, usar Tailscale (sem abrir porta no roteador);
   - **App Android em Avalonia** (`ManfraTorrent.Android`, `net10.0-android`), só como controle remoto: abas Descobrir e
     Downloads (polling de poucos segundos), leitor de QR para parear, e "Enviar para o PC" no menu de compartilhar do
     Android (intent para links `magnet:` e `.torrent`). Sem motor de torrent no celular;
   - **Refatoração:** extrair uma interface (ex.: `ITorrentBackend`) para os ViewModels funcionarem com o motor local
     (desktop) ou com o desktop remoto (Android), reaproveitando ViewModels e estilos;
   - visual mobile: seguir o protótipo em `docs/prototipo-android/`.
6. Escolher arquivos antes de baixar (`SetFilePriorityAsync(file, Priority.DoNotDownload)`).
7. Registrar o app como handler de `magnet:` (Windows: registro em `HKCU\Software\Classes\magnet`; Linux: `.desktop` com `MimeType=x-scheme-handler/magnet`) e aceitar o link via `args` em `Program.cs`.
8. Ícone na bandeja / iniciar minimizado.
9. Amarrar o tráfego a uma interface (VPN).
10. (Por último, se fizer falta) Player embutido (LibVLCSharp) aproveitando o modo streaming.
