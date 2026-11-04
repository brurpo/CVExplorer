namespace CVExplorer.Host.Features.Media;

/// <summary>
/// Monta a linha de comando do ffmpeg. Funcao pura (sem I/O) para poder ser testada
/// e conferida contra as pegadinhas conhecidas: -pix_fmt obrigatorio no 4444/HQX,
/// timecode, VFR, tags de cor e container.
/// </summary>
public static class FfmpegCommandBuilder
{
    public static List<string> Build(MediaInfo info, VideoPlan plan, string entrada, string saida)
    {
        var args = new List<string>
        {
            "-hide_banner", "-nostdin", "-y", "-loglevel", "error",
            "-progress", "pipe:1", "-nostats",
            "-i", entrada,
            "-map", "0:v:0",
        };

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

        if (plan.Encoder == "prores_ks")
        {
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
        }
        else
        {
            args.Add("-profile:v");
            args.Add(plan.Perfil switch
            {
                "dnxhr_hqx" => "dnxhr_hqx",
                "dnxhr_lb" => "dnxhr_lb",
                "dnxhr_sq" => "dnxhr_sq",
                _ => "dnxhr_sq",
            });
            args.Add("-pix_fmt");
            args.Add(plan.PixFmt);
        }

        if (info.TemAudio)
        {
            args.Add("-c:a");
            args.Add("pcm_s24le");
            args.Add("-ar");
            args.Add("48000");
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

        if (!string.IsNullOrWhiteSpace(info.EspacoCor))
        {
            args.Add("-colorspace");
            args.Add(info.EspacoCor!);
        }

        if (!string.IsNullOrWhiteSpace(info.FaixaCor))
        {
            args.Add("-color_range");
            args.Add(info.FaixaCor!);
        }

        // Timecode do original: sem isso o AE importa com TC zerado.
        if (!string.IsNullOrWhiteSpace(info.Timecode))
        {
            args.Add("-timecode");
            args.Add(info.Timecode!);
        }

        args.Add("-movflags");
        args.Add(plan.Encoder == "prores_ks" ? "+faststart+write_colr" : "+faststart");

        args.Add(saida);
        return args;
    }
}
