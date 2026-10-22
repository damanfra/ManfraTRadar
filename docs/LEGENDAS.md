# Plano — busca de legendas PT-BR

Objetivo: achar legenda em português do Brasil para vídeos **já baixados**, gastando o mínimo possível da cota
diária dos serviços (o OpenSubtitles hoje libera poucos downloads por dia).

## Princípios (decididos com o Daniel)

1. **Nada automático.** Só começa quando o usuário clica num download **concluído** e confirma. Sem buscas em
   segundo plano, sem novas tentativas sozinhas, sem buscar durante o download.
2. **Mostrar antes de gastar.** Antes de qualquer acesso à internet, o app mostra o que vai fazer
   (quantas buscas, em quais serviços, quantos downloads de cota) e espera o "Buscar".
3. **Buscar é barato, baixar é caro.** No OpenSubtitles, o que conta na cota diária é o **download** da legenda,
   não a busca. Então: uma busca por arquivo, o usuário escolhe, e só então um download.
4. **Verificar o que já existe antes de ir à internet.**

## Limites dos serviços

| Serviço | Conta | Download de legendas por dia | Observações |
|---|---|---|---|
| OpenSubtitles.com (API REST) | sem login | 5 | precisa de chave de API (consumer) mesmo sem login |
| OpenSubtitles.com | conta gratuita (login) | 20 | o login também é limitado: guardar e reaproveitar o token |
| OpenSubtitles.com | VIP | bem maior | pago |
| SubDL (API) | conta gratuita + chave de API | sem cota diária publicada | entrega `.zip` (às vezes com a temporada inteira) |

O antigo opensubtitles.org (API XML-RPC) foi desativado; usar só a API REST do opensubtitles.com.

## Fluxo na tela

1. **Downloads** → item concluído → menu "⋯" → **Buscar legenda PT-BR…** (só aparece para torrents com vídeo).
2. **Etapa 1 — verificação local (sem internet)**, para cada vídeo do torrent:
   - ignora amostras (`sample` no nome ou menos de 100 MB) e extras;
   - já existe `.srt` em português ao lado do vídeo ou dentro do torrent (nome com `pt-BR`, `ptbr`, `por`, `portuguese`,
     `brazil`...) → **"já tem legenda"**;
   - MKV com faixa de legenda embutida em português (`Language` = `por` ou `LanguageIETF` = `pt-BR`) →
     **"legenda embutida encontrada"** (o usuário pode buscar mesmo assim);
   - calcula o **hash do OpenSubtitles** (tamanho + primeiros e últimos 64 KB; leitura local, instantânea).
3. **Etapa 2 — confirmação**: "Vai consultar OpenSubtitles (1 busca) e SubDL (1 busca) para 1 vídeo.
   Nenhum download de legenda até você escolher. Cota OpenSubtitles hoje: 18 de 20." → **Buscar** / Cancelar.
4. **Etapa 3 — resultados**, ordenados:
   1. ★ **Sincronizada com seu arquivo** (`moviehash_match` do OpenSubtitles): mesma versão do vídeo, encaixe garantido;
   2. mesmo grupo/release do nome do arquivo (ex.: `WEB-DL`, `BTM`, `TOXIC`);
   3. mais baixadas / de autores confiáveis.

   Escondidas por padrão: tradução automática ou por IA (`machine_translated`, `ai_translated`).
   Cada linha diz se o download **usa cota** (OpenSubtitles) ou **não usa** (SubDL).
5. **Etapa 4 — baixar a escolhida**: salva ao lado do vídeo, converte para **UTF-8** (muitas PT-BR vêm em
   Windows-1252 e ficam com acentos quebrados) e marca o download com "legenda ✓".

**Séries / temporada inteira:** a lista mostra cada episódio sem legenda, com caixa de seleção. Antes de confirmar,
o app soma: "12 episódios = 12 downloads da cota (você tem 18 hoje)". Se o SubDL tiver o **pacote da temporada**
(um `.zip` só), ele aparece primeiro, porque não gasta cota.

## Regras contra excesso de requisições

- **Uma busca por serviço por arquivo.** No OpenSubtitles, `moviehash` + `query` + `languages=pt-br` vão na
  mesma chamada; não faz uma busca por hash e outra por nome.
- **Cache local** (`subtitles-cache.json` na pasta de dados), por hash + tamanho do arquivo:
  - resultado da busca guardado por 7 dias; reabrir a tela mostra o cache, sem internet;
  - "nada encontrado" também fica guardado, para não repetir a busca à toa;
  - botão explícito **"Buscar de novo"** para ignorar o cache.
- **Controle da cota:** o OpenSubtitles devolve, a cada download, quantos restam e quando reinicia
  (`remaining`, `reset_time_utc`). O app guarda isso, mostra na tela e **bloqueia** novos downloads do
  OpenSubtitles quando chegar a zero, até o horário de reinício.
- **Login do OpenSubtitles uma vez só:** guardar o token e reutilizar até expirar (o próprio login tem limite).
- **Erro 429 (muitas requisições):** respeitar o `Retry-After`, uma única nova tentativa, sem laço.
- Cabeçalhos obrigatórios: `Api-Key` e `User-Agent: ManfraTRadar v<versão>`.

## Nome do arquivo de legenda

Padrão `<nome do vídeo>.pt-BR.srt` (VLC, MPC e Jellyfin reconhecem). Como o Daniel usa **Plex**, a terminação
fica configurável: `.pt-BR.srt` / `.pt.srt` / `.por.srt`. Testar no Plex qual ele identifica como
"Português (Brasil)" e deixar essa como padrão.

## Configurações (seção "Legendas")

- OpenSubtitles: chave de API (consumer), usuário e senha (opcionais; sem login a cota cai para 5/dia).
- SubDL: chave de API.
- Terminação do arquivo (`.pt-BR` / `.pt` / `.por`).
- Aceitar português de Portugal se não houver PT-BR (padrão: não).
- Mostrar traduções automáticas/IA (padrão: não).

## Código (proposta)

- `Core/Subtitles/ISubtitleProvider` — `SearchAsync(VideoInfo, ct)` e `DownloadAsync(SubtitleCandidate, ct)`;
  informa `ConsumesQuota`.
- `Core/Subtitles/OpenSubtitlesProvider`, `SubDlProvider`.
- `Core/Subtitles/VideoInspector` — lista vídeos do torrent, descarta amostras, acha `.srt` existentes, lê faixas
  de legenda do MKV (leitor EBML mínimo, só o elemento `Tracks`) e calcula o hash do OpenSubtitles.
- `Core/Subtitles/SubtitleCache` e `QuotaTracker` — persistência em JSON na pasta de dados.
- `Core/Subtitles/SubtitleWriter` — detecta a codificação, converte para UTF-8 e grava com o nome configurado.
- App: `SubtitleDialog` (as 4 etapas acima) aberto pelo menu do item em Downloads.

## Fases

1. Verificação local + OpenSubtitles (busca por hash, escolha manual, download, cota, cache, UTF-8).
2. SubDL (inclusive pacotes de temporada) e seleção de episódios.
3. Ajuste do nome do arquivo depois do teste no Plex.

## O que o Daniel precisa providenciar

- Conta gratuita no **opensubtitles.com** e uma **chave de API** em *opensubtitles.com/consumers*.
- Conta gratuita no **subdl.com** e a **chave de API** em *subdl.com/panel/api*.
- Na fase 3, testar no Plex qual terminação ele reconhece como PT-BR.
