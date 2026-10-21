# Montando o ambiente do zero

Guia para preparar uma máquina nova (outra rede, outro computador) e rodar o ManfraTRadar.
Siga na ordem; cada seção diz como conferir se deu certo.

---

## 1. Resumo do que precisa estar instalado

| O quê | Versão | Para quê | Obrigatório? |
|---|---|---|---|
| Git | qualquer recente | clonar o repositório | sim |
| .NET SDK | **10.0** (LTS) | compilar e rodar | sim |
| IDE | Visual Studio 2026 / Rider / VS Code + C# Dev Kit | editar e depurar | recomendado |
| Extensão Avalonia | da sua IDE | preview dos `.axaml` | opcional |
| Jackett **ou** Prowlarr | última | fontes Torznab (indexadores) | opcional |
| Docker | qualquer recente | alternativa para rodar Jackett/Prowlarr | opcional |

Tudo o mais (Avalonia, MonoTorrent, CommunityToolkit.Mvvm) vem pelo NuGet no primeiro `dotnet restore`.

---

## 2. Windows 10/11

### 2.1 Ferramentas

```powershell
winget install Git.Git
winget install Microsoft.DotNet.SDK.10
# IDE (escolha uma)
winget install JetBrains.Rider
winget install Microsoft.VisualStudioCode   # + extensões "C# Dev Kit" e "Avalonia for VSCode"
```

Visual Studio: instale pelo site da Microsoft com a carga de trabalho ".NET desktop development"
e, opcionalmente, a extensão "Avalonia for Visual Studio".

Sem winget: baixe o SDK em https://dotnet.microsoft.com/download/dotnet/10.0.

Conferir:

```powershell
dotnet --list-sdks     # deve listar 10.0.x
git --version
```

### 2.2 Clonar e rodar

```powershell
git clone https://github.com/damanfra/ManfraTRadar.git
cd ManfraTRadar
dotnet restore
dotnet build
dotnet run --project src/ManfraTorrent.App
```

No Visual Studio / Rider: abra `ManfraTorrent.sln` e defina `ManfraTorrent.App` como projeto de inicialização.

### 2.3 Firewall

Na primeira execução o Windows pergunta se libera o `ManfraTRadar.exe` (ou `dotnet.exe` em modo debug) na rede.
**Libere em redes privadas.** Sem isso o app ainda baixa, mas não recebe conexões de entrada e fica mais lento.

Para liberar manualmente a porta padrão (55123, TCP e UDP):

```powershell
New-NetFirewallRule -DisplayName "ManfraTRadar TCP" -Direction Inbound -Protocol TCP -LocalPort 55123 -Action Allow
New-NetFirewallRule -DisplayName "ManfraTRadar UDP" -Direction Inbound -Protocol UDP -LocalPort 55123 -Action Allow
```

---

## 3. Linux (Ubuntu/Debian e derivados)

### 3.1 Ferramentas

.NET 10: siga https://learn.microsoft.com/dotnet/core/install/linux para a sua distro
(pacote `dotnet-sdk-10.0` pelo gerenciador da distro ou pelo repositório da Microsoft).

Bibliotecas que o Avalonia usa no X11:

```bash
sudo apt install git libx11-6 libice6 libsm6 libfontconfig1
```

Conferir: `dotnet --list-sdks` deve listar 10.0.x.

### 3.2 Clonar e rodar

```bash
git clone https://github.com/damanfra/ManfraTRadar.git
cd ManfraTRadar
dotnet run --project src/ManfraTorrent.App
```

### 3.3 Firewall (se usar ufw)

```bash
sudo ufw allow 55123/tcp
sudo ufw allow 55123/udp
```

---

## 4. Rede

O BitTorrent funciona melhor quando outros pares conseguem conectar em você.

- **Porta de escuta:** padrão **55123** (TCP para pares, UDP para DHT). Dá para mudar em *Fontes e ajustes*.
- **UPnP / NAT-PMP:** o MonoTorrent tenta abrir a porta no roteador sozinho (`AllowPortForwarding = true`).
  Se o roteador tiver UPnP desligado, faça o redirecionamento manual da porta para o IP da máquina.
- **Rede corporativa / Wi-Fi público:** costumam bloquear P2P. Sintoma: os downloads ficam em
  "Buscando metadados…" com 0 pares. Use outra rede ou uma VPN.
- **VPN:** se usar, prefira uma com *port forwarding* e configure a mesma porta no app.
- **Proxy HTTP:** o app usa o proxy do sistema para as buscas (Internet Archive / Torznab).
  O tráfego BitTorrent em si não passa por proxy HTTP.

Teste rápido: na aba Descobrir, filtre "Filmes" no Internet Archive e baixe algo pequeno.
Os itens do Internet Archive também têm *web seed* (o próprio servidor do Archive), então baixam mesmo com poucos pares.

---

## 5. Fontes Torznab (Jackett ou Prowlarr) — opcional

O app só traz o Internet Archive embutido. Os outros indexadores vêm de um Jackett ou Prowlarr rodando na mesma máquina (ou em outra da rede).

### 5.1 Instalar

Windows:

```powershell
winget install Jackett.Jackett          # porta 9117
# ou
winget install TeamProwlarr.Prowlarr    # porta 9696
```

Linux ou qualquer SO com Docker:

```bash
docker run -d --name jackett -p 9117:9117 -v jackett-config:/config lscr.io/linuxserver/jackett:latest
# ou
docker run -d --name prowlarr -p 9696:9696 -v prowlarr-config:/config lscr.io/linuxserver/prowlarr:latest
```

### 5.2 Configurar

1. Abra http://127.0.0.1:9117 (Jackett) ou http://127.0.0.1:9696 (Prowlarr).
2. Adicione os indexadores que quiser.
3. Copie a **API key** (topo da página no Jackett; *Settings → General* no Prowlarr).
4. No ManfraTRadar, vá em **Fontes e ajustes**:
   - Jackett, todos os indexadores juntos: `http://127.0.0.1:9117/api/v2.0/indexers/all/results/torznab/api`
   - Prowlarr, um indexador: `http://127.0.0.1:9696/{id}/api` (o `{id}` aparece na URL do indexador)
5. Cole a URL e a API key, clique em **Testar** e depois em **Salvar**.

Se o Jackett/Prowlarr estiver em outra máquina, troque `127.0.0.1` pelo IP dela e libere a porta no firewall de lá.

---

## 6. Onde o app guarda as coisas

| O quê | Windows | Linux |
|---|---|---|
| Configurações (`settings.json`) | `%APPDATA%\ManfraTRadar` | `~/.config/ManfraTRadar` |
| Estado do motor (`engine.state`) | mesma pasta | mesma pasta |
| Cache do MonoTorrent (`cache/`: fast resume, metadados, DHT) | mesma pasta | mesma pasta |
| Downloads (padrão) | `%USERPROFILE%\Downloads\ManfraTRadar` | `~/Downloads/ManfraTRadar` |

Para migrar downloads em andamento de uma máquina para outra: copie a pasta de dados **e** a pasta de downloads,
e mantenha o mesmo caminho de downloads (ou o app vai verificar os arquivos de novo).

Para recomeçar do zero: feche o app e apague a pasta de dados.

---

## 7. Gerar executável

```bash
# Windows: um .exe só, sem precisar do .NET instalado na máquina de destino
dotnet publish src/ManfraTorrent.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/win-x64

# Linux
dotnet publish src/ManfraTorrent.App -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o publish/linux-x64
chmod +x publish/linux-x64/ManfraTRadar
```

---

## 8. Problemas comuns

| Sintoma | Causa provável | O que fazer |
|---|---|---|
| `NETSDK1045: The current .NET SDK does not support targeting .NET 10.0` | SDK antigo | Instale o SDK 10, ou troque `net10.0` por `net8.0`/`net9.0` em `Directory.Build.props` |
| `dotnet restore` falha / timeout | Proxy ou firewall bloqueando `api.nuget.org` | Configure o proxy (`HTTPS_PROXY`) ou use outra rede |
| Download parado em "Buscando metadados…" | Rede bloqueia P2P/DHT | Outra rede ou VPN; confira a porta no firewall |
| Descobrir mostra "falhou: Internet Archive" | Sem acesso a archive.org | Confira a internet / proxy |
| Torznab "401" ou "Invalid API key" | API key errada | Copie de novo do Jackett/Prowlarr |
| Torznab "Connection refused" | Jackett/Prowlarr parado ou porta errada | Abra o painel dele no navegador para conferir |
| No Linux, a janela não abre e o erro cita `libICE`/`libSM` | Bibliotecas X11 faltando | `sudo apt install libice6 libsm6 libfontconfig1` |
| "Address already in use" ao iniciar | Outro cliente torrent usando a porta 55123 | Mude a porta em *Fontes e ajustes* ou feche o outro cliente |
