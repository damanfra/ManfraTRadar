# ManfraTRadar

Cliente torrent desktop para uso pessoal, com uma aba **Descobrir** que mostra o que está em alta nas suas fontes.
Um único código roda no **Windows** e no **Linux**.

> **v0.1 — ainda não compilado.** O primeiro passo numa máquina nova é `dotnet build` e corrigir o que aparecer.

## Stack

| Parte | Tecnologia |
|---|---|
| Runtime | .NET 10 (LTS) |
| Interface | Avalonia 11.3 (Fluent, tema escuro) + CommunityToolkit.Mvvm 8.4 |
| Motor BitTorrent | MonoTorrent 3.0.2 (DHT, PEX, magnet, web seeds, download em ordem) |
| Fontes "em alta" | Internet Archive (embutido) + qualquer endpoint Torznab (Jackett / Prowlarr) |

## Início rápido

```bash
git clone https://github.com/damanfra/ManfraTRadar.git
cd ManfraTRadar
dotnet run --project src/ManfraTorrent.App
```

Precisa do [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

## Documentação

- [docs/AMBIENTE.md](docs/AMBIENTE.md): montar tudo do zero numa máquina e rede novas (Windows e Linux, firewall, portas, Jackett/Prowlarr, problemas comuns).
- [docs/ARQUITETURA.md](docs/ARQUITETURA.md): stack completa com versões, estrutura dos projetos, fluxos, persistência e decisões.
- [CLAUDE.md](CLAUDE.md): contexto, convenções e cuidados para continuar o desenvolvimento.
- [docs/prototipo-android/](docs/prototipo-android/): protótipo de tela original (mobile).

## Telas

- **Descobrir:** busca em todas as fontes ativas ao mesmo tempo. Sem termo de busca, mostra o que está em alta.
  Tem filtro por categoria, ordenação e painel de detalhes (seeders/leechers, saúde do swarm, infohash).
  "Baixar em ordem" permite assistir enquanto baixa.
- **Downloads:** progresso, velocidade, ETA, pausar/retomar, abrir pasta, remover com ou sem apagar os arquivos.
  Aceita link magnet, URL de `.torrent` ou arquivo `.torrent` local.
- **Fontes e ajustes:** pasta de destino, porta, limites de velocidade e cadastro de fontes, com botão **Testar**.
