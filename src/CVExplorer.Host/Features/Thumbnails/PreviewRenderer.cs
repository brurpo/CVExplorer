using ImageMagick;

namespace CVExplorer.Host.Features.Thumbnails;

/// <summary>
/// Pre-visualizacao em tamanho grande, entregue como bytes ao WebView.
/// Evita depender de host virtual (que falha em alguns formatos/mapeamentos) e
/// limita o tamanho da imagem para nao trafegar um RAW de 60 MB para a interface.
/// </summary>
public static class PreviewRenderer
{
    private const long LimiteBytes = 6 * 1024 * 1024;

    private static readonly HashSet<string> Vetoriais = new(StringComparer.OrdinalIgnoreCase)
    {
        ".svg",
    };

    public static (string? Mime, string? Dados) Render(string caminho, int ladoMaximo)
    {
        try
        {
            if (!File.Exists(caminho))
            {
                return (null, null);
            }

            ladoMaximo = Math.Clamp(ladoMaximo, 256, 4096);

            if (Vetoriais.Contains(Path.GetExtension(caminho)))
            {
                // SVG se desenha nativo no navegador: manda o arquivo como esta.
                var bytes = File.ReadAllBytes(caminho);
                return ("image/svg+xml", Convert.ToBase64String(bytes));
            }

            using var imagem = new MagickImage(caminho);
            imagem.AutoOrient();

            if (imagem.Width > ladoMaximo || imagem.Height > ladoMaximo)
            {
                imagem.Thumbnail(new MagickGeometry((uint)ladoMaximo, (uint)ladoMaximo) { Greater = true });
            }

            var extensao = Path.GetExtension(caminho).ToLowerInvariant();
            if (extensao is ".exr" or ".hdr")
            {
                imagem.AutoLevel();
                imagem.ColorSpace = ColorSpace.sRGB;
            }

            // Alpha se perde em JPEG; nesses casos manda PNG.
            var temAlpha = imagem.HasAlpha;
            imagem.Format = temAlpha ? MagickFormat.Png : MagickFormat.Jpeg;
            imagem.Quality = 88;
            imagem.Strip();

            using var memoria = new MemoryStream();
            imagem.Write(memoria);

            if (memoria.Length > LimiteBytes && !temAlpha)
            {
                memoria.SetLength(0);
                imagem.Quality = 70;
                using var menor = new MagickImage(imagem.ToByteArray())
                {
                    Quality = 70,
                };
                menor.Thumbnail(new MagickGeometry(
                    (uint)Math.Min(imagem.Width, ladoMaximo / 2),
                    (uint)Math.Min(imagem.Height, ladoMaximo / 2)) { Greater = true });
                menor.Format = MagickFormat.Jpeg;
                menor.Write(memoria);
            }

            var mime = temAlpha ? "image/png" : "image/jpeg";
            return (mime, Convert.ToBase64String(memoria.ToArray()));
        }
        catch (Exception ex)
        {
            Log.Error($"Falha ao gerar pre-visualizacao de {caminho}", ex);
            return (null, null);
        }
    }
}
