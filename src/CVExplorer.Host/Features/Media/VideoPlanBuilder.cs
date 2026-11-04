using System.Globalization;

namespace CVExplorer.Host.Features.Media;

public sealed class VideoPlan
{
    public string Perfil { get; set; } = "prores422lt";

    public string NomeExibicao { get; set; } = "ProRes 422 LT";

    public string Encoder { get; set; } = "prores_ks";

    public string PixFmt { get; set; } = "yuv422p10le";

    public int ProfileValue { get; set; } = 1;

    public bool ComAlpha { get; set; }

    public string Container { get; set; } = ".mov";

    public long TamanhoEstimadoBytes { get; set; }

    /// <summary>Bitrate de video da origem em Mbps (0 = desconhecido).</summary>
    public double OrigemMbps { get; set; }

    public double AlvoMbps { get; set; }

    /// <summary>Por que este perfil foi escolhido (mostrado na interface).</summary>
    public string? Motivo { get; set; }

    public List<string> Avisos { get; } = new();
}

public sealed class VideoEstimate
{
    public string Perfil { get; set; } = string.Empty;

    public string Nome { get; set; } = string.Empty;

    public long Bytes { get; set; }

    public string Texto { get; set; } = string.Empty;
}

/// <summary>
/// Escolhe o codec de edicao a partir da ORIGEM, nao de um padrao fixo.
///
/// Regra: ProRes e intra-frame, entao gasta bem mais bits que um MXF long-GOP.
/// Para nao inflar arquivo sem ganho real, o alvo e o menor perfil intra-frame que
/// ainda entrega ~2x o bitrate de video da origem (margem pratica de transparencia).
/// 10 bits, alpha ou 4:4:4 continuam exigindo perfil alto, porque ai ha informacao de verdade.
/// </summary>
public static class VideoPlanBuilder
{
    private sealed record PerfilInfo(
        string Nome,
        string Encoder,
        string PixFmt,
        int Profile,
        int Mbps1080p30,
        string Descricao);

    private static readonly (string Chave, PerfilInfo Info)[] Ordem =
    {
        ("proxy", new PerfilInfo("ProRes 422 Proxy", "prores_ks", "yuv422p10le", 0, 38, "leve, para offline")),
        ("dnxhr_lb", new PerfilInfo("DNxHR LB", "dnxhd", "yuv422p", 0, 45, "menor tamanho possivel em intra-frame")),
        ("prores422lt", new PerfilInfo("ProRes 422 LT", "prores_ks", "yuv422p10le", 1, 102, "transparente para origens de ate ~50 Mbps")),
        ("prores422", new PerfilInfo("ProRes 422", "prores_ks", "yuv422p10le", 2, 147, "transparente para origens de ate ~75 Mbps")),
        ("prores422hq", new PerfilInfo("ProRes 422 HQ", "prores_ks", "yuv422p10le", 3, 220, "para origens de alto bitrate ou 10 bits")),
        ("prores4444", new PerfilInfo("ProRes 4444", "prores_ks", "yuv444p10le", 4, 330, "4:4:4 e alpha")),
        ("dnxhr_hqx", new PerfilInfo("DNxHR HQX", "dnxhd", "yuv422p10le", 0, 220, "12 bits, fluxo Avid")),
        ("dnxhr_sq", new PerfilInfo("DNxHR SQ", "dnxhd", "yuv422p", 0, 145, "8 bits, decodificacao muito rapida")),
    };

    public const long MargemSegurancaBytes = 1L * 1024 * 1024 * 1024;

    private static PerfilInfo Get(string chave) =>
        Ordem.First(p => p.Chave.Equals(chave, StringComparison.OrdinalIgnoreCase)).Info;

    public static bool Existe(string? chave) =>
        !string.IsNullOrWhiteSpace(chave)
        && Ordem.Any(p => p.Chave.Equals(chave, StringComparison.OrdinalIgnoreCase));

    public static VideoPlan Choose(MediaInfo info, string? perfilSolicitado, long espacoLivre)
    {
        var pedido = (perfilSolicitado ?? "auto").Trim().ToLowerInvariant();
        var chave = Existe(pedido) && pedido != "auto" ? pedido : EscolherAutomatico(info);
        var perfil = Get(chave);

        var plano = new VideoPlan
        {
            Perfil = chave,
            NomeExibicao = perfil.Nome,
            Encoder = perfil.Encoder,
            ProfileValue = perfil.Profile,
            PixFmt = perfil.PixFmt,
            OrigemMbps = info.BitrateVideoMbps,
            AlvoMbps = perfil.Mbps1080p30 * FatorResolucao(info) * FatorFps(info),
        };

        // Alpha nao existe em 4:2:2: nesse caso 4444 nunca e opcional.
        plano.ComAlpha = info.TemAlpha;
        if (info.TemAlpha)
        {
            var p4444 = Get("prores4444");
            chave = "prores4444";
            plano.Perfil = chave;
            plano.NomeExibicao = p4444.Nome;
            plano.Encoder = p4444.Encoder;
            plano.ProfileValue = p4444.Profile;
            plano.PixFmt = "yuva444p10le";
            plano.AlvoMbps = p4444.Mbps1080p30 * FatorResolucao(info) * FatorFps(info);
            plano.Motivo = "A origem tem canal alpha; so ProRes 4444 preserva transparencia.";
        }

        plano.TamanhoEstimadoBytes = Estimate(perfil.Mbps1080p30, info);

        if (plano.Motivo is null)
        {
            plano.Motivo = ExplicarEscolha(info, chave, perfil);
        }

        AjustarPorEspaco(plano, info, espacoLivre);
        AdicionarAvisos(plano, info);
        return plano;
    }

    /// <summary>
    /// Alvo = ~2x o bitrate de video da origem, arredondado para o perfil que atende.
    /// Sem bitrate conhecido, fica no perfil padrao de edicao.
    /// </summary>
    private static string EscolherAutomatico(MediaInfo info)
    {
        if (info.TemAlpha || info.AltaCromaticidade)
        {
            return "prores4444";
        }

        // 10 bits (ou mais) tem informacao que perfil 8-bits perderia.
        if (info.DezBits || info.Hdr)
        {
            return "prores422hq";
        }

        var origem = info.BitrateVideoMbps;
        if (origem <= 0)
        {
            return "prores422lt";
        }

        var alvo = origem * 2;

        if (alvo <= 45) return "dnxhr_lb";
        if (alvo <= 102) return "prores422lt";
        if (alvo <= 147) return "prores422";
        return "prores422hq";
    }

    private static string ExplicarEscolha(MediaInfo info, string chave, PerfilInfo perfil)
    {
        if (info.BitrateVideoMbps <= 0)
        {
            return $"Bitrate da origem desconhecido; {perfil.Nome} e o padrao seguro para edicao.";
        }

        var explicacao = info.DezBits
            ? $"Origem em 10 bits: {perfil.Nome} preserva a profundidade de cor."
            : $"Origem de {info.BitrateVideoMbps:0} Mbps: {perfil.Nome} entrega "
              + $"{perfil.Mbps1080p30 * FatorResolucao(info) * FatorFps(info):0} Mbps, "
              + "cerca de 2x a origem, sem perda visivel.";

        return explicacao;
    }

    private static void AjustarPorEspaco(VideoPlan plano, MediaInfo info, long espacoLivre)
    {
        if (espacoLivre <= 0 || plano.TamanhoEstimadoBytes + MargemSegurancaBytes <= espacoLivre)
        {
            return;
        }

        var original = plano.NomeExibicao;
        foreach (var (chave, perfil) in Ordem)
        {
            var estimado = Estimate(perfil.Mbps1080p30, info);
            if (estimado + MargemSegurancaBytes > espacoLivre)
            {
                continue;
            }

            plano.Perfil = chave;
            plano.NomeExibicao = perfil.Nome;
            plano.Encoder = perfil.Encoder;
            plano.ProfileValue = perfil.Profile;
            plano.PixFmt = plano.ComAlpha && chave == "prores4444" ? "yuva444p10le" : perfil.PixFmt;
            plano.TamanhoEstimadoBytes = estimado;
            plano.Avisos.Add($"Espaco insuficiente para {original}. Usando {perfil.Nome}.");
            return;
        }

        var piso = Get("dnxhr_lb");
        plano.Perfil = "dnxhr_lb";
        plano.NomeExibicao = piso.Nome;
        plano.Encoder = piso.Encoder;
        plano.PixFmt = piso.PixFmt;
        plano.TamanhoEstimadoBytes = Estimate(piso.Mbps1080p30, info);
        plano.Avisos.Add(
            $"Espaco livre insuficiente (~{FormatBytes(espacoLivre)}). Libere espaco antes de converter.");
    }

    private static void AdicionarAvisos(VideoPlan plano, MediaInfo info)
    {
        // Deixa explicito por que o arquivo cresce: e o preco do intra-frame.
        if (info.BitrateVideoMbps > 0 && plano.AlvoMbps > info.BitrateVideoMbps * 1.5)
        {
            var fator = plano.AlvoMbps / info.BitrateVideoMbps;
            plano.Avisos.Add(
                $"{info.Codec.ToUpperInvariant()} da origem e long-GOP ({info.BitrateVideoMbps:0} Mbps); "
                + $"{plano.NomeExibicao} e intra-frame e fica ~{fator:0.#}x maior. "
                + "E o preco de cada quadro ser decodificado sozinho (scrub instantaneo no AE).");
        }

        if (info.Hdr)
        {
            plano.Avisos.Add("Origem HDR: primarias/transferencia sao preservadas, "
                             + "mas os metadados estaticos HDR10 (MaxCLL) nao sao gravados pelo ffmpeg.");
        }

        if (info.DolbyVision)
        {
            plano.Avisos.Add("Dolby Vision detectado: o RPU nao existe em ProRes. O original sera mantido intacto.");
        }

        if (info.Largura % 2 != 0 || info.Altura % 2 != 0)
        {
            plano.Avisos.Add($"Dimensoes impares ({info.Largura}x{info.Altura}): o ffmpeg ajusta para par sem avisar.");
        }

        if (info.Vfr)
        {
            plano.Avisos.Add("Taxa de quadros variavel: preservada sem duplicar/descartar quadros.");
        }
    }

    /// <summary>Tamanho estimado de cada perfil, para a interface mostrar a conta antes de converter.</summary>
    public static List<VideoEstimate> Estimates(MediaInfo info) =>
        Ordem.Select(p => new VideoEstimate
        {
            Perfil = p.Chave,
            Nome = p.Info.Nome,
            Bytes = Estimate(p.Info.Mbps1080p30, info),
            Texto = FormatBytes(Estimate(p.Info.Mbps1080p30, info)),
        }).ToList();

    public static long Estimate(int mbps1080p30, MediaInfo info)
    {
        if (info.DuracaoSegundos <= 0)
        {
            return 0;
        }

        var videoMbps = mbps1080p30 * FatorResolucao(info) * FatorFps(info);
        var audioMbps = info.TemAudio ? Math.Max(1.152 * Math.Max(1, info.AudioCanais), 1.152) : 0;

        return (long)(info.DuracaoSegundos * (videoMbps + audioMbps) * 1_000_000 / 8);
    }

    private static double FatorResolucao(MediaInfo info)
    {
        var pixels = Math.Max(info.Largura * info.Altura, 1);
        return pixels / (1920.0 * 1080.0);
    }

    private static double FatorFps(MediaInfo info) => info.Fps > 0 ? info.Fps / 29.97 : 1;

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "—";
        }

        string[] unidades = { "B", "KB", "MB", "GB", "TB" };
        double valor = bytes;
        var unidade = 0;
        while (valor >= 1024 && unidade < unidades.Length - 1)
        {
            valor /= 1024;
            unidade++;
        }

        return string.Create(CultureInfo.GetCultureInfo("pt-BR"), $"{valor:0.#} {unidades[unidade]}");
    }
}
