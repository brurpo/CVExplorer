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

    /// <summary>pcm24 | pcm16 | aac</summary>
    public string Audio { get; set; } = "pcm24";

    public long TamanhoEstimadoBytes { get; set; }

    /// <summary>Bitrate de video da origem em Mbps (0 = desconhecido).</summary>
    public double OrigemMbps { get; set; }

    public double AlvoMbps { get; set; }

    /// <summary>Por que este perfil foi escolhido (mostrado na interface).</summary>
    public string? Motivo { get; set; }

    /// <summary>Desentrelaçar na conversão (origem entrelaçada).</summary>
    public bool Desentrelacar { get; set; }

    /// <summary>Filtro de desentrelaçamento; o host escolhe o melhor que o ffmpeg oferece.</summary>
    public string FiltroDesentrelace { get; set; } = "bwdif=mode=send_frame:parity=auto:deint=interlaced";

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
        string Descricao,
        string Container = ".mov",
        string Audio = "pcm24",
        bool Fixo = false,
        bool Alpha = false);

    /// <summary>
    /// Escada usada pela escolha automatica e pelo ajuste de espaco. Só perfis de
    /// edicao entram aqui: os presets "de entrega" (MXF, XDCAM, H.264, DXV3) são
    /// escolhidos à mão no menu de contexto e não podem ser auto-selecionados.
    /// </summary>
    private static readonly (string Chave, PerfilInfo Info)[] Ordem =
    {
        ("proxy", new PerfilInfo("ProRes 422 Proxy", "prores_ks", "yuv422p10le", 0, 38, "leve, para offline")),
        ("dnxhr_lb", new PerfilInfo("DNxHR LB", "dnxhd", "yuv422p", 0, 45, "menor tamanho possivel em intra-frame")),
        ("prores422lt", new PerfilInfo("ProRes 422 LT", "prores_ks", "yuv422p10le", 1, 102, "transparente para origens de ate ~50 Mbps")),
        ("prores422", new PerfilInfo("ProRes 422", "prores_ks", "yuv422p10le", 2, 147, "transparente para origens de ate ~75 Mbps")),
        ("prores422hq", new PerfilInfo("ProRes 422 HQ", "prores_ks", "yuv422p10le", 3, 220, "para origens de alto bitrate ou 10 bits")),
        ("prores4444", new PerfilInfo("ProRes 4444", "prores_ks", "yuva444p10le", 4, 330, "4:4:4 e alpha", Alpha: true)),
        ("dnxhr_hqx", new PerfilInfo("DNxHR HQX", "dnxhd", "yuv422p10le", 0, 220, "12 bits, fluxo Avid")),
        ("dnxhr_sq", new PerfilInfo("DNxHR SQ", "dnxhd", "yuv422p", 0, 145, "8 bits, decodificacao muito rapida")),
    };

    /// <summary>Presets de conversao manual (menu de contexto): codec + container + audio.</summary>
    private static readonly (string Chave, PerfilInfo Info)[] Extras =
    {
        ("prores422_mov", new PerfilInfo("ProRes 422 — .mov", "prores_ks", "yuv422p10le", 2, 147, "entrega em QuickTime")),
        ("prores4444_mov", new PerfilInfo("ProRes 4444 (alpha) — .mov", "prores_ks", "yuva444p10le", 4, 330, "canal alpha preservado", Alpha: true)),
        ("dnxhr_sq_mov", new PerfilInfo("DNxHR SQ — .mov", "dnxhd", "yuv422p", 0, 138, "Avid/fluxo Windows")),
        ("dnxhr_hq_mov", new PerfilInfo("DNxHR HQ — .mov", "dnxhd", "yuv422p", 0, 208, "8 bits de alta qualidade")),
        ("dnxhr_sq_mxf", new PerfilInfo("DNxHR SQ — .mxf", "dnxhd", "yuv422p", 0, 138, "MXF OP1a para Avid", ".mxf")),
        ("dnxhr_hq_mxf", new PerfilInfo("DNxHR HQ — .mxf", "dnxhd", "yuv422p", 0, 208, "MXF OP1a para Avid", ".mxf")),
        ("xdcam", new PerfilInfo("XDCAM HD422 50 Mbps — .mxf", "mpeg2video", "yuv422p", 0, 50, "padrão de emissora", ".mxf", "pcm24", Fixo: true)),
        ("dxv3", new PerfilInfo("DXV3 — .mov", "dxv", "rgba", 0, 185, "Resolume, playback em tempo real", ".mov", "pcm16")),
        ("dxv3_alpha", new PerfilInfo("DXV3 (alpha) — .mov", "dxv", "rgba", 0, 370, "playback com transparência (DXT5)", ".mov", "pcm16", Alpha: true)),
        ("hap_alpha", new PerfilInfo("HAP Alpha — .mov", "hap", "rgba", 0, 370, "playback com transparência (DXT5)", ".mov", "pcm16", Alpha: true)),
        ("h264", new PerfilInfo("H.264 — .mp4", "h264", "yuv420p", 0, 20, "entrega/aprovação", ".mp4", "aac")),
    };

    private static IEnumerable<(string Chave, PerfilInfo Info)> Todos => Ordem.Concat(Extras);

    public const long MargemSegurancaBytes = 1L * 1024 * 1024 * 1024;

    private static PerfilInfo Get(string chave) =>
        Todos.First(p => p.Chave.Equals(chave, StringComparison.OrdinalIgnoreCase)).Info;

    public static bool Existe(string? chave) =>
        !string.IsNullOrWhiteSpace(chave)
        && Todos.Any(p => p.Chave.Equals(chave, StringComparison.OrdinalIgnoreCase));

    /// <summary>Presets de conversao manual, para o menu de contexto.</summary>
    public static IReadOnlyList<(string Chave, string Nome, string Container)> Presets() =>
        Extras.Select(p => (p.Chave, p.Info.Nome, p.Info.Container)).ToList();

    public static VideoPlan Choose(MediaInfo info, string? perfilSolicitado, long espacoLivre)
    {
        var pedido = (perfilSolicitado ?? "auto").Trim().ToLowerInvariant();
        var explicito = Existe(pedido) && pedido != "auto";
        var chave = explicito ? pedido : EscolherAutomatico(info);
        var perfil = Get(chave);

        var plano = new VideoPlan
        {
            Perfil = chave,
            NomeExibicao = perfil.Nome,
            Encoder = perfil.Encoder,
            ProfileValue = perfil.Profile,
            PixFmt = perfil.PixFmt,
            Container = perfil.Container,
            Audio = perfil.Audio,
            OrigemMbps = info.BitrateVideoMbps,
            AlvoMbps = Alvo(perfil, info),
            Desentrelacar = info.Interlaced,
        };

        // Alpha nao existe em 4:2:2. Perfil de EDICAO nunca desiste do alpha: sobe para 4444.
        // Ja um preset manual (menu "Converter para", ex. H.264) e escolha explicita de entrega
        // e deve ser respeitado — nesse caso o alpha e descartado com aviso.
        var ehPresetManual = Extras.Any(p => p.Chave.Equals(chave, StringComparison.OrdinalIgnoreCase));
        plano.ComAlpha = perfil.Alpha;

        if (info.TemAlpha && !ehPresetManual)
        {
            var p4444 = Get("prores4444");
            chave = "prores4444";
            plano.Perfil = chave;
            plano.NomeExibicao = p4444.Nome;
            plano.Encoder = p4444.Encoder;
            plano.ProfileValue = p4444.Profile;
            plano.PixFmt = "yuva444p10le";
            plano.Container = p4444.Container;
            plano.Audio = p4444.Audio;
            plano.ComAlpha = true;
            plano.AlvoMbps = Alvo(p4444, info);
            plano.Motivo = "A origem tem canal alpha; so ProRes 4444 preserva transparencia.";
        }
        else if (info.TemAlpha && ehPresetManual && !plano.ComAlpha)
        {
            plano.Avisos.Add(
                $"{perfil.Nome} não guarda canal alpha: a transparência da origem será descartada. "
                + "Use HAP Alpha (playback) ou ProRes 4444 (edição) se precisar manter.");
        }
        else if (!info.TemAlpha && !ehPresetManual && plano.ComAlpha)
        {
            // 4:4:4 sem alpha: o quarto canal so pesaria ~25% a mais no arquivo.
            plano.PixFmt = plano.PixFmt.Replace("yuva", "yuv", StringComparison.Ordinal);
            plano.ComAlpha = false;
        }

        // Estimativa do perfil que realmente vai ser usado (o alpha pode ter trocado o perfil).
        plano.TamanhoEstimadoBytes = Estimate(Get(plano.Perfil), info);

        if (plano.Motivo is null)
        {
            plano.Motivo = explicito
                ? $"Preset escolhido à mão — {perfil.Descricao}."
                : ExplicarEscolha(info, chave, perfil);
        }

        // Perfil escolhido a mao nao e trocado por espaco: o usuario pediu aquele formato.
        if (!explicito)
        {
            AjustarPorEspaco(plano, info, espacoLivre);
        }

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
            // Nunca desce para um perfil sem alpha quando a origem tem transparencia.
            if (plano.ComAlpha && !PixFmtComAlpha(perfil.PixFmt))
            {
                continue;
            }

            var estimado = Estimate(perfil, info);
            if (estimado + MargemSegurancaBytes > espacoLivre)
            {
                continue;
            }

            plano.Perfil = chave;
            plano.NomeExibicao = perfil.Nome;
            plano.Encoder = perfil.Encoder;
            plano.ProfileValue = perfil.Profile;
            plano.PixFmt = perfil.PixFmt;
            plano.ComAlpha = plano.PixFmt.StartsWith("yuva", StringComparison.Ordinal);
            plano.Container = perfil.Container;
            plano.Audio = perfil.Audio;
            plano.TamanhoEstimadoBytes = estimado;
            plano.Avisos.Add($"Espaco insuficiente para {original}. Usando {perfil.Nome}.");
            return;
        }

        var piso = Get("dnxhr_lb");
        plano.Perfil = "dnxhr_lb";
        plano.NomeExibicao = piso.Nome;
        plano.Encoder = piso.Encoder;
        plano.PixFmt = piso.PixFmt;
        plano.ComAlpha = false;
        plano.Container = piso.Container;
        plano.Audio = piso.Audio;
        plano.TamanhoEstimadoBytes = Estimate(piso, info);
        plano.Avisos.Add(
            $"Espaco livre insuficiente (~{FormatBytes(espacoLivre)}). Libere espaco antes de converter.");
    }

    private static void AdicionarAvisos(VideoPlan plano, MediaInfo info)
    {
        // Deixa explicito por que o arquivo cresce: e o preco do intra-frame.
        // Formato de entrega long-GOP (XDCAM, H.264) nao ganha esse aviso: ele nao e intra-frame.
        var intraFrame = plano.Encoder is "prores_ks" or "dnxhd" or "dxv";
        if (intraFrame && info.BitrateVideoMbps > 0 && plano.AlvoMbps > info.BitrateVideoMbps * 1.5)
        {
            var fator = plano.AlvoMbps / info.BitrateVideoMbps;

            // Só culpe o long-GOP se a origem realmente for long-GOP.
            plano.Avisos.Add(EhLongGop(info.Codec)
                ? $"{info.Codec.ToUpperInvariant()} da origem é long-GOP ({info.BitrateVideoMbps:0} Mbps); "
                  + $"{plano.NomeExibicao} é intra-frame e fica ~{fator:0.#}x maior. "
                  + "É o preço de cada quadro ser decodificado sozinho (scrub instantâneo no AE)."
                : $"{plano.NomeExibicao} usa ~{plano.AlvoMbps:0} Mbps contra {info.BitrateVideoMbps:0} Mbps "
                  + $"da origem: fica ~{fator:0.#}x maior, mas cada quadro é independente "
                  + "(scrub instantâneo no AE).");
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

        if (plano.Desentrelacar)
        {
            plano.Avisos.Add(
                $"Origem entrelaçada ({info.FieldOrder}): a conversão desentrelaça, "
                + "senão cada quadro vira dois campos serrilhados no AE.");
        }
    }

    /// <summary>Tamanho estimado de cada perfil, para a interface mostrar a conta antes de converter.</summary>
    public static List<VideoEstimate> Estimates(MediaInfo info) =>
        Todos.Select(p => new VideoEstimate
        {
            Perfil = p.Chave,
            Nome = p.Info.Nome,
            Bytes = Estimate(p.Info, info),
            Texto = FormatBytes(Estimate(p.Info, info)),
        }).ToList();

    private static long Estimate(PerfilInfo perfil, MediaInfo info) =>
        Estimate(perfil.Mbps1080p30, info, perfil.Fixo);

    public static long Estimate(int mbps1080p30, MediaInfo info, bool fixo = false)
    {
        if (info.DuracaoSegundos <= 0)
        {
            return 0;
        }

        // Preset com bitrate fechado (XDCAM CBR) nao acompanha resolucao nem fps.
        var videoMbps = fixo ? mbps1080p30 : mbps1080p30 * FatorResolucao(info) * FatorFps(info);
        var audioMbps = info.TemAudio ? Math.Max(1.152 * Math.Max(1, info.AudioCanais), 1.152) : 0;

        return (long)(info.DuracaoSegundos * (videoMbps + audioMbps) * 1_000_000 / 8);
    }

    /// <summary>Codecs que guardam diferenças entre quadros (o oposto de intra-frame).</summary>
    private static bool EhLongGop(string codec) =>
        codec is "h264" or "hevc" or "mpeg1video" or "mpeg2video" or "mpeg4" or "vp8" or "vp9"
            or "av1" or "theora" or "wmv2" or "wmv3" or "vc1" or "msmpeg4v3";

    /// <summary>O pix_fmt escolhido carrega um canal de transparência de verdade.</summary>
    private static bool PixFmtComAlpha(string pixFmt) =>
        pixFmt.StartsWith("yuva", StringComparison.Ordinal)
        || pixFmt.StartsWith("rgba", StringComparison.Ordinal)
        || pixFmt.StartsWith("bgra", StringComparison.Ordinal)
        || pixFmt.StartsWith("argb", StringComparison.Ordinal)
        || pixFmt.StartsWith("abgr", StringComparison.Ordinal)
        || pixFmt.StartsWith("gbrap", StringComparison.Ordinal);

    private static double Alvo(PerfilInfo perfil, MediaInfo info) =>
        perfil.Fixo ? perfil.Mbps1080p30 : perfil.Mbps1080p30 * FatorResolucao(info) * FatorFps(info);

    private static double FatorResolucao(MediaInfo info)    {
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
