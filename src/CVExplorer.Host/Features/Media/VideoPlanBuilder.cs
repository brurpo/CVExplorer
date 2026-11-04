using System.Globalization;

namespace CVExplorer.Host.Features.Media;

public sealed class VideoPlan
{
    public string Perfil { get; set; } = "prores422hq";

    public string NomeExibicao { get; set; } = "ProRes 422 HQ";

    public string Encoder { get; set; } = "prores_ks";

    public string PixFmt { get; set; } = "yuv422p10le";

    public int ProfileValue { get; set; } = 3;

    public bool ComAlpha { get; set; }

    public string Container { get; set; } = ".mov";

    public long TamanhoEstimadoBytes { get; set; }

    public List<string> Avisos { get; } = new();
}

/// <summary>
/// Decide o codec de edicao. Regra: ProRes 422 HQ por padrao (intra-frame, 10 bits,
/// decodificacao nativa no After Effects no Windows); ProRes 4444 quando ha alpha ou
/// a origem ja e 4:4:4/RGB; e cai para perfis menores quando o disco nao comporta.
/// </summary>
public static class VideoPlanBuilder
{
    private static readonly Dictionary<string, (string Nome, string Encoder, string PixFmt, int Profile, int Mbps1080p30)> Perfis =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["prores422hq"] = ("ProRes 422 HQ", "prores_ks", "yuv422p10le", 3, 220),
            ["prores422"] = ("ProRes 422", "prores_ks", "yuv422p10le", 2, 147),
            ["prores4444"] = ("ProRes 4444", "prores_ks", "yuv444p10le", 4, 330),
            ["proxy"] = ("ProRes 422 Proxy", "prores_ks", "yuv422p10le", 0, 38),
            ["dnxhr_hqx"] = ("DNxHR HQX", "dnxhd", "yuv422p10le", 0, 220),
        };

    public const long MargemSegurancaBytes = 2L * 1024 * 1024 * 1024;

    public static VideoPlan Choose(MediaInfo info, string? perfilSolicitado, long espacoLivre)
    {
        var querAlpha = info.TemAlpha;
        var perfil = (perfilSolicitado ?? "auto").Trim().ToLowerInvariant();

        if (perfil == "auto" || !Perfis.ContainsKey(perfil))
        {
            perfil = querAlpha || info.AltaCromaticidade ? "prores4444" : "prores422hq";
        }

        // Alpha nao existe em 4:2:2: nesse caso 4444 nunca e opcional.
        if (querAlpha && perfil != "prores4444")
        {
            perfil = "prores4444";
        }

        var definicao = Perfis[perfil];
        var plan = new VideoPlan
        {
            Perfil = perfil,
            NomeExibicao = definicao.Nome,
            Encoder = definicao.Encoder,
            ProfileValue = definicao.Profile,
            ComAlpha = querAlpha,
            PixFmt = querAlpha
                ? "yuva444p10le"
                : perfil == "prores4444" ? "yuv444p10le" : definicao.PixFmt,
        };

        plan.TamanhoEstimadoBytes = Estimate(definicao.Mbps1080p30, info);

        // Sem espaco: reduz o perfil (proxy e o piso) em vez de encher o disco.
        if (espacoLivre > 0 && plan.TamanhoEstimadoBytes + MargemSegurancaBytes > espacoLivre)
        {
            foreach (var alternativa in new[] { "prores422", "proxy" })
            {
                var alt = Perfis[alternativa];
                var estimado = Estimate(alt.Mbps1080p30, info);
                if (estimado + MargemSegurancaBytes <= espacoLivre)
                {
                    plan.Avisos.Add(
                        $"Espaco insuficiente para {definicao.Nome}. Usando {alt.Nome} (~{FormatBytes(estimado)}).");
                    perfil = alternativa;
                    plan.Perfil = alternativa;
                    plan.NomeExibicao = alt.Nome;
                    plan.ProfileValue = alt.Profile;
                    plan.PixFmt = alt.PixFmt;
                    plan.TamanhoEstimadoBytes = estimado;
                    break;
                }
            }

            if (plan.TamanhoEstimadoBytes + MargemSegurancaBytes > espacoLivre)
            {
                // Nada cabe: usa o menor perfil e avisa que ainda assim falta espaco.
                var piso = Perfis["proxy"];
                plan.Perfil = "proxy";
                plan.NomeExibicao = piso.Nome;
                plan.ProfileValue = piso.Profile;
                plan.PixFmt = piso.PixFmt;
                plan.TamanhoEstimadoBytes = Estimate(piso.Mbps1080p30, info);

                plan.Avisos.Add(
                    $"Espaco livre insuficiente (~{FormatBytes(espacoLivre)}). Libere espaco antes de converter.");
            }
        }

        if (info.Hdr)
        {
            plan.Avisos.Add("Origem HDR: as cores (primarias/transferencia) sao preservadas, "
                            + "mas os metadados estaticos HDR10 (MaxCLL) nao sao gravados pelo ffmpeg.");
        }

        if (info.DolbyVision)
        {
            plan.Avisos.Add("Dolby Vision detectado: o RPU nao existe em ProRes. "
                            + "O original sera mantido intacto.");
        }

        if (info.Largura % 2 != 0 || info.Altura % 2 != 0)
        {
            plan.Avisos.Add($"Dimensoes impares ({info.Largura}x{info.Altura}): o ffmpeg ajusta para par sem avisar.");
        }

        if (info.Vfr)
        {
            plan.Avisos.Add("Taxa de quadros variavel: preservada sem duplicar/descartar quadros.");
        }

        return plan;
    }

    public static long Estimate(int mbps1080p30, MediaInfo info)
    {
        if (info.DuracaoSegundos <= 0)
        {
            return 0;
        }

        var pixels = Math.Max(info.Largura * info.Altura, 1);
        var fatorResolucao = pixels / (1920.0 * 1080.0);
        var fatorFps = info.Fps > 0 ? info.Fps / 29.97 : 1;

        // ProRes/DNxHR sao VBR: a tabela da Apple/Avid e o teto pratico.
        var mbps = mbps1080p30 * fatorResolucao * fatorFps;
        return (long)(info.DuracaoSegundos * mbps * 1_000_000 / 8);
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.GetCultureInfo("pt-BR"), $"{value:0.#} {units[unit]}");
    }
}
