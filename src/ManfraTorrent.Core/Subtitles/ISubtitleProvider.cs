namespace ManfraTorrent.Core.Subtitles;

public interface ISubtitleProvider
{
    string Name { get; }

    /// <summary>Se baixar uma legenda gasta a cota diária do serviço (mostrado ao usuário antes de baixar).</summary>
    string QuotaNote { get; }

    /// <summary>Uma única chamada de busca para o vídeo. Buscar não gasta cota de download.</summary>
    Task<List<SubtitleCandidate>> SearchAsync(VideoInfo video, bool acceptPtPt, CancellationToken ct);

    /// <summary>Baixa a legenda escolhida e devolve o texto bruto (bytes do .srt).</summary>
    Task<byte[]> DownloadAsync(SubtitleCandidate candidate, VideoInfo video, CancellationToken ct);
}
