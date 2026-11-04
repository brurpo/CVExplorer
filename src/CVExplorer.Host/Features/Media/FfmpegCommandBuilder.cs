namespace CVExplorer.Host.Features.Media;

/// <summary>
/// Monta a linha de comando do ffmpeg. Funcao pura (sem I/O) para poder ser testada
/// e conferida contra as pegadinhas conhecidas: -pix_fmt obrigatorio no 4444/HQX,
/// timecode, VFR, tags de cor e container.
/// </summary>
public static class FfmpegCommandBuilder
{
    /// <summary>
    /// Entrada que nao e um arquivo de video: sequencia de imagens via demuxer concat.
    /// </summary>
    public sealed record EntradaSequencia(int Fps);

    public static List<string> Build(
        MediaInfo info,
        VideoPlan plan,
        string entrada,
        string saida,
        EntradaSequencia? sequencia = null)
    {
        var args = new List<string>
        {
            "-hide_banner", "-nostdin", "-y", "-loglevel", "error",
            "-progress", "pipe:1", "-nostats",
        };

        if (sequencia is null)
        {
            args.Add("-i");
            args.Add(entrada);
        }
        else
        {
            args.Add("-f");
            args.Add("concat");
            args.Add("-safe");
            args.Add("0");
            args.Add("-i");
            args.Add(entrada);

            // O concat entrega os quadros no intervalo declarado na lista; o -r fecha o CFR.
            args.Add("-fps_mode");
            args.Add("cfr");
            args.Add("-r");
            args.Add(sequencia.Fps.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        args.Add("-map");
        args.Add("0:v:0");

        if (info.TemAudio)
        {
            args.Add("-map");
            args.Add("0:a?");
        }

        // Ajuste de dimensao impar: o ffmpeg cortaria silenciosamente para par.
        if (info.Largura % 2 != 0 || info.Altura % 2 != 0)
        {
            args.Add("-vf");
            args.Add("scale=trunc(iw/2)*2:trunc(ih/2)*2");
        }

        args.Add("-c:v");
        args.Add(plan.Encoder);

        switch (plan.Encoder)
        {
            case "prores_ks":
                args.Add("-profile:v");
                args.Add(plan.ProfileValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
                args.Add("-vendor");
                args.Add("apl0");
                args.Add("-pix_fmt");
                args.Add(plan.PixFmt);

                if (plan.ComAlpha)
                {
                    args.Add("-alpha_bits");
                    args.Add("16");
                }

                break;

            case "dnxhd":
                args.Add("-profile:v");
                args.Add(plan.Perfil switch
                {
                    "dnxhr_hqx" => "dnxhr_hqx",
                    "dnxhr_lb" => "dnxhr_lb",
                    "dnxhr_hq" or "dnxhr_hq_mov" or "dnxhr_hq_mxf" => "dnxhr_hq",
                    _ => "dnxhr_sq",
                });
                args.Add("-pix_fmt");
                args.Add(plan.PixFmt);
                break;

            case "mpeg2video":
                // XDCAM HD422: 50 Mbps CBR, GOP 12, 8 bits 4:2:2. E o que emissora espera.
                args.AddRange(new[]
                {
                    "-b:v", "50M", "-minrate", "50M", "-maxrate", "50M",
                    "-bufsize", "17825792", "-g", "12", "-bf", "2",
                    "-pix_fmt", plan.PixFmt,
                });
                break;

            case "dxv":
                args.Add("-pix_fmt");
                args.Add(plan.PixFmt);
                break;

            default:
                // H.264: encoder vem resolvido pelo host (libx264/nvenc/qsv/amf/mf/openh264).
                args.Add("-pix_fmt");
                args.Add(plan.PixFmt);
                args.Add("-b:v");
                args.Add($"{Math.Round(plan.AlvoMbps):0}M");
                break;
        }

        if (info.TemAudio)
        {
            args.Add("-c:a");
            switch (plan.Audio)
            {
                case "aac":
                    args.Add("aac");
                    args.Add("-b:a");
                    args.Add("320k");
                    break;
                case "pcm16":
                    args.Add("pcm_s16le");
                    args.Add("-ar");
                    args.Add("48000");
                    break;
                default:
                    args.Add("pcm_s24le");
                    args.Add("-ar");
                    args.Add("48000");
                    break;
            }
        }
        else
        {
            args.Add("-an");
        }

        // VFR (celular/tela) precisa passar sem duplicar quadros.
        if (info.Vfr)
        {
            args.Add("-fps_mode");
            args.Add("passthrough");
        }

        // Tags de cor do original. Sequência de imagens não traz metadado confiável (PNG é
        // "gbr", que o encoder não aceita) e o ffmpeg taggeia o YUV de saída sozinho.
        if (sequencia is null)
        {
            if (!string.IsNullOrWhiteSpace(info.Primarias))
            {
                args.Add("-color_primaries");
                args.Add(info.Primarias!);
            }

            if (!string.IsNullOrWhiteSpace(info.Transferencia))
            {
                args.Add("-color_trc");
                args.Add(info.Transferencia!);
            }

            if (EspacoCorValido(info.EspacoCor))
            {
                args.Add("-colorspace");
                args.Add(info.EspacoCor!);
            }

            if (!string.IsNullOrWhiteSpace(info.FaixaCor))
            {
                args.Add("-color_range");
                args.Add(info.FaixaCor!);
            }
        }

        // Timecode do original: sem isso o AE importa com TC zerado. (MXF guarda em outro campo.)
        if (!string.IsNullOrWhiteSpace(info.Timecode) && plan.Container != ".mxf")
        {
            args.Add("-timecode");
            args.Add(info.Timecode!);
        }

        if (plan.Container == ".mxf")
        {
            // OP1a: um arquivo unico, como camera/emissora entrega.
            args.Add("-f");
            args.Add("mxf");
        }
        else
        {
            args.Add("-movflags");
            args.Add(plan.Encoder == "prores_ks" ? "+faststart+write_colr" : "+faststart");
        }

        args.Add(saida);
        return args;
    }

    /// <summary>
    /// "gbr"/"rgb" são espaço de cor de origem RGB e o encoder de vídeo não aceita o nome;
    /// qualquer valor fora da lista oficial seria rejeitado com "Undefined constant".
    /// </summary>
    private static bool EspacoCorValido(string? valor) =>
        !string.IsNullOrWhiteSpace(valor)
        && !valor.Equals("gbr", StringComparison.OrdinalIgnoreCase)
        && !valor.Equals("rgb", StringComparison.OrdinalIgnoreCase)
        && !valor.Equals("unknown", StringComparison.OrdinalIgnoreCase);
}
