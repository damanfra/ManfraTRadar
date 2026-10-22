# Legendas PT-BR (sob demanda)

> **Status (22/10/2026):** fases 1 e parte da 2 implementadas (OpenSubtitles + SubDL, um vídeo por vez),
> ainda **não testadas** com chaves reais. Falta: seleção de vários episódios de uma vez e o teste no Plex.

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
| OpenSubtitles.com (API REST) | consumer gratuito (chave de API) | **5** (confirmado na página de consumers, 22/10/2026) | 5 requisições/s por IP; `dev_mode` libera 100/dia só para testes |
| OpenSubtitles.com | login de conta gratuita | cota da conta (a Central de Ajuda fala em 20; na prática pode ser 5) | o app mostra o `remaining` real depois do 1º download; o login também é limitado: token reaproveitado por 12 h |
| OpenSubtitles.com | VIP | bem maior | pago |
| SubDL (API) | conta gratuita + chave de API | tem limite diário, valor não publicado (erro 429 `daily_limit`) | entrega `.zip` (às vezes com a temporada inteira) |

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
   O **termo da busca é editável** (título e ano; em séries, o episódio vem do nome do arquivo): dá para encurtar ou
   corrigir e buscar de novo. Cada termo tem seu próprio resultado guardado.
4. **Etapa 3 — resultados**, ordenados:
   1. ★ **Sincronizada com seu arquivo** (`moviehash_match` do OpenSubtitles): mesma versão do vídeo, encaixe garantido;
   2. mesmo grupo/release do nome do arquivo (ex.: `WEB-DL`, `BTM`, `TOXIC`);
   3. mais baixadas / de autores confiáveis.

   Escondidas por padrão: tradução automática ou por IA (`machine_translated`, `ai_translated`) e **resultados de
   outros títulos** (a busca por nome dos serviços é frouxa: qualquer filme com "of"/"the" no nome aparece; o app
   exige todas as palavras do termo no nome da release ou no título informado pelo serviço; a sincronizada pelo hash
   sempre fica). O cabeçalho diz quantas foram escondidas.
   Cada linha diz se o download **usa cota** (OpenSubtitles) ou **não usa** (SubDL).
5. **Etapa 4 — baixar a escolhida**: salva ao lado do vídeo, converte para **UTF-8** (muitas PT-BR vêm em
   Windows-1252 e ficam com acentos quebrados) e marca o download com "legenda ✓".

**Séries / temporada inteira:** a lista mostra cada episódio sem legenda, com caixa de seleção. Antes de confirmar,
o app soma: "12 episódios = 12 downloads da cota (você tem 18 hoje)". Se o SubDL tiver o **pacote da temporada**
(um `.zip` só), ele aparece primeiro, porque não gasta a cota do OpenSubtitles. *(Ainda não implementado:
hoje é um vídeo por vez; o pacote do SubDL já é aberto e o episódio certo é extraído.)*

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

- OpenSubtitles: chave de API (consumer), usuário e senha (opcionais; a cota real aparece depois do 1º download).
- SubDL: chave de API.
- Terminação do arquivo (`.pt-BR` / `.pt` / `.por`).
- Aceitar português de Portugal se não houver PT-BR (padrão: não).
- Mostrar traduções automáticas/IA (padrão: não).

## Código

- `Core/Subtitles/ISubtitleProvider` — `SearchAsync` (1 chamada) e `DownloadAsync`; `QuotaNote` diz se gasta cota.
- `Core/Subtitles/OpenSubtitlesProvider` — busca com `moviehash`+`query`+`languages=pt-br` numa chamada (parâmetros em
  ordem alfabética, minúsculos), `ai_translated=exclude`, ignora `machine_translated`; login guardado por 12 h
  (`base_url` do login vira o host); `POST /download` atualiza `remaining`/`reset_time_utc`; 406 = cota esgotada.
- `Core/Subtitles/SubDlProvider` — `languages=BR_PT` (+`PT` se aceitar Portugal), `type=movie|tv`, temporada/episódio;
  baixa o `.zip` de `dl.subdl.com` e extrai o `.srt` do episódio certo.
- `Core/Subtitles/VideoInspector` — vídeos do torrent (sem amostras < 100 MB), `.srt` existentes (ignora os de
  outros episódios), faixas de legenda do MKV (`MkvReader`, leitor EBML mínimo até `Tracks`) e hash do OpenSubtitles.
- `Core/Subtitles/ReleaseNameParser` — título, ano, SxxEyy e grupo a partir do nome do arquivo.
- `Core/Subtitles/SubtitleRanker` — hash ★ > mesmo grupo > nome parecido > PT-BR > downloads.
- `Core/Subtitles/SubtitleService` — provedores configurados, cache 7 dias e estado do OpenSubtitles em
  `subtitles.json` (pasta de dados); só guarda o cache se ao menos um serviço respondeu.
- `Core/Subtitles/SubtitleWriter` — BOM / UTF-8 estrito / Windows-1252 → grava UTF-8 com BOM.
- App: `SubtitleWindow` + `SubtitleViewModel` (as 4 etapas), aberta por *Downloads → ⋯ → Buscar legenda PT-BR…*;
  configurações em *Fontes e ajustes → Legendas PT-BR*.

## Fases

1. Verificação local + OpenSubtitles (busca por hash, escolha manual, download, cota, cache, UTF-8).
2. SubDL (inclusive pacotes de temporada) e seleção de episódios.
3. Ajuste do nome do arquivo depois do teste no Plex.

## O que o Daniel precisa providenciar

- Conta gratuita no **opensubtitles.com** e uma **chave de API** em *opensubtitles.com/consumers*.
- Conta gratuita no **subdl.com** e a **chave de API** em *subdl.com/panel/api*.
- Na fase 3, testar no Plex qual terminação ele reconhece como PT-BR.
