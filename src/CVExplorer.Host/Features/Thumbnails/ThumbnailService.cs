using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ImageMagick;
using CVExplorer.Host.Features.FileSystem;
using CVExplorer.Host.Features.Media;
using CVExplorer.Host.Native;

namespace CVExplorer.Host.Features.Thumbnails;

public sealed class ThumbnailResult
{
    public string Chave { get; set; } = string.Empty;

    public bool Pronta { get; set; }

    /// <summary>
    /// Formato que a interface desenha (e devolve com thumb.salvar):
    /// "3d" para modelos (three.js) e "pdf" para .ai/.pdf compativeis (pdf.js).
    /// </summary>
    public string? Renderizar { get; set; }
}

/// <summary>
/// Cadeia de miniaturas: cache em disco -> Shell do Windows -> decoders proprios
/// (Magick.NET, .blend, ffmpeg) -> icone do tipo. Tudo em WebP, com chave
/// (caminho + tamanho + data) para invalidar sozinho quando o arquivo muda.
/// </summary>
public sealed class ThumbnailService
{
    private static readonly HashSet<string> ImagensMagick = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tif", ".tiff", ".tga",
        ".psd", ".psb", ".svg", ".exr", ".hdr", ".dds", ".ico", ".heic", ".jxl", ".dpx",
    };

    private static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mxf", ".avi", ".mkv", ".wmv", ".webm", ".m4v", ".mpg", ".mpeg", ".mts", ".m2ts",
    };

    /// <summary>Formatos que o proprio app renderiza (three.js no WebView2).</summary>
    private static readonly HashSet<string> Modelos3D = new(StringComparer.OrdinalIgnoreCase)
    {
        ".obj", ".fbx", ".glb", ".gltf", ".stl", ".usdz", ".dae",
        // USD e Alembic não têm loader no navegador: o Blender converte uma vez para GLB.
        ".usd", ".usda", ".usdc", ".abc",
    };

    public static bool EhModelo3D(string caminho) => Modelos3D.Contains(Path.GetExtension(caminho));

    /// <summary>.ai salvo com "PDF compatible" e PDF de verdade: da para desenhar sem Illustrator.</summary>
    public static bool EhDocumentoPdf(string caminho)
    {
        var ext = Path.GetExtension(caminho);
        if (!ext.Equals(".ai", StringComparison.OrdinalIgnoreCase)
            && !ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(caminho);
            Span<byte> cabecalho = stackalloc byte[5];
            return stream.Read(cabecalho) == 5
                   && cabecalho[0] == (byte)'%'
                   && cabecalho[1] == (byte)'P'
                   && cabecalho[2] == (byte)'D'
                   && cabecalho[3] == (byte)'F';
        }
        catch
        {
            return false;
        }
    }

    private readonly AppSettings _settings;
    private readonly FfmpegLocator _ffmpeg;
    // Decodificacao em paralelo: 32 nucleos nao precisam de 32 decoders, mas 4 e pouco.
    private readonly SemaphoreSlim _limite = new(Math.Clamp(Environment.ProcessorCount / 4, 4, 12));
    private readonly ConcurrentDictionary<string, Task<ThumbnailResult>> _emAndamento = new();

    /// <summary>Geração em curso por caminho, para poder cancelar quando o arquivo vai ser movido/apagado.</summary>
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancelaveis = new();

    public ThumbnailService(AppSettings settings, FfmpegLocator ffmpeg)
    {
        _settings = settings;
        _ffmpeg = ffmpeg;
        Directory.CreateDirectory(AppPaths.ThumbsDir);
    }

    public static string UrlFor(string chave) =>
        $"https://{AppPaths.ThumbsVirtualHost}/{chave}.webp";

    /// <summary>
    /// Muda quando o pipeline de miniaturas muda (novo decoder, nova ordem, render no app):
    /// invalida o cache antigo sozinho, sem o usuario precisar limpar nada.
    /// </summary>
    private const int VersaoCache = 3;

    public static string Chave(string caminho, long tamanho, DateTime modificado, int size)
    {
        var raw = $"v{VersaoCache}|{caminho.ToLowerInvariant()}|{tamanho}|{modificado.Ticks}|{size}";
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..20].ToLowerInvariant();
    }

    public async Task<ThumbnailResult> RequestAsync(
        string caminho,
        long tamanho,
        DateTime modificado,
        int size,
        CancellationToken ct)
    {
        size = Math.Clamp(size, 96, 512);
        var chave = Chave(caminho, tamanho, modificado, size);
        var destino = Path.Combine(AppPaths.ThumbsDir, chave + ".webp");

        if (File.Exists(destino))
        {
            return new ThumbnailResult { Chave = chave, Pronta = true };
        }

        // Modelo 3D: quem desenha e a interface (three.js). O host so entrega o arquivo
        // e guarda o WebP que voltar em thumb.salvar.
        if (EhModelo3D(caminho))
        {
            // Arquivo gigante (3D scan de varios GB) travaria o parse: fica o icone de tipo.
            var cabe = tamanho <= 350L * 1024 * 1024;
            return new ThumbnailResult { Chave = chave, Pronta = false, Renderizar = cabe ? "3d" : null };
        }

        // Compactado usa o icone do proprio app: a miniatura do Shell (pasta com ziper)
        // vem colorida e destoa do resto da grade.
        if (ArchiveService.EhCompactado(caminho))
        {
            return new ThumbnailResult { Chave = chave, Pronta = false };
        }

        // Item dentro de um compactado não existe no disco: decodificador nenhum alcança,
        // então fica o ícone do tipo (sem tentar Shell/Magick e sujar o log).
        if (ArchiveService.Separar(caminho) is not null)
        {
            return new ThumbnailResult { Chave = chave, Pronta = false };
        }

        // .ai/.pdf: pdf.js desenha a primeira pagina no WebView2, sem Illustrator instalado.
        if (EhDocumentoPdf(caminho))
        {
            return new ThumbnailResult { Chave = chave, Pronta = false, Renderizar = "pdf" };
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(2));

        var chaveCancelamento = Normalizar(caminho) + "|" + chave;
        _cancelaveis[chaveCancelamento] = cts;

        var task = _emAndamento.GetOrAdd(chave, _ => GenerateAsync(caminho, destino, size, chave, cts.Token));
        try
        {
            return await task.ConfigureAwait(false);
        }
        finally
        {
            if (task.IsCompleted)
            {
                _emAndamento.TryRemove(chave, out _);
                if (_cancelaveis.TryRemove(chaveCancelamento, out var dono))
                {
                    dono.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Cancela as miniaturas em geração destes caminhos. Sem isso, o ffmpeg/Magick que
    /// está a ler o arquivo impede o Windows de apagá-lo ou movê-lo por alguns segundos.
    /// </summary>
    public void Liberar(IEnumerable<string> caminhos)
    {
        foreach (var caminho in caminhos ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(caminho))
            {
                continue;
            }

            var chaveCaminho = Normalizar(caminho);
            foreach (var par in _cancelaveis.ToArray())
            {
                if (!par.Key.StartsWith(chaveCaminho + "|", StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    par.Value.Cancel();
                }
                catch
                {
                    // já cancelado
                }
            }
        }
    }

    private static string Normalizar(string caminho) =>
        Path.GetFullPath(caminho).ToLowerInvariant();

    private async Task<ThumbnailResult> GenerateAsync(
        string caminho,
        string destino,
        int size,
        string chave,
        CancellationToken ct)
    {
        await _limite.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var png = await RenderAsync(caminho, size, ct).ConfigureAwait(false);
            if (png is null || png.Length == 0)
            {
                return new ThumbnailResult { Chave = chave, Pronta = false };
            }

            Encode(destino, png, size);
            return new ThumbnailResult { Chave = chave, Pronta = true };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error($"Miniatura falhou: {caminho}", ex);
            return new ThumbnailResult { Chave = chave, Pronta = false };
        }
        finally
        {
            _limite.Release();
        }
    }

    private async Task<byte[]?> RenderAsync(string caminho, int size, CancellationToken ct)
    {
        var ext = Path.GetExtension(caminho);

        // 1) .blend: o preview embutido no arquivo e instantaneo e nao depende de
        // handler do Windows nem de abrir o Blender. Vem antes do Shell de proposito.
        if (ext.Equals(".blend", StringComparison.OrdinalIgnoreCase))
        {
            var preview = await Task.Run(() => ReadBlend(caminho), ct).ConfigureAwait(false);
            if (preview is not null)
            {
                return preview;
            }
        }

        // 2) Shell: melhor resultado para formatos com handler instalado (PSD/AI/PDF).
        var shell = await ShellThumbnail.GetPngAsync(caminho, size, ct).ConfigureAwait(false);
        if (shell is not null)
        {
            return shell;
        }

        // 3) Decoders proprios.
        if (ImagensMagick.Contains(ext))
        {
            var magick = await Task.Run(() => RenderMagick(caminho, size, ext), ct).ConfigureAwait(false);
            if (magick is not null)
            {
                return magick;
            }
        }

        if (Videos.Contains(ext))
        {
            var frame = await RenderVideoFrameAsync(caminho, size, ct).ConfigureAwait(false);
            if (frame is not null)
            {
                return frame;
            }
        }

        // 4) Icone do tipo.
        return await Task.Run(() => ShellThumbnail.GetTypeIcon(caminho), ct).ConfigureAwait(false);
    }

    private static byte[]? ReadBlend(string caminho)
    {
        try
        {
            using var stream = File.OpenRead(caminho);
            var preview = BlendPreviewReader.Read(stream);
            if (preview is null)
            {
                return null;
            }

            using var bitmap = new Bitmap(preview.Largura, preview.Altura, PixelFormat.Format32bppArgb);
            var data = bitmap.LockBits(
                new Rectangle(0, 0, preview.Largura, preview.Altura),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);

            try
            {
                // O .blend guarda o preview de baixo para cima (padrao de imagem do Blender)
                // e em RGBA, enquanto o GDI+ espera BGRA: inverte a linha e troca R/B.
                var stride = data.Stride;
                var destino = new byte[stride * preview.Altura];
                for (var y = 0; y < preview.Altura; y++)
                {
                    var origem = (preview.Altura - 1 - y) * preview.Largura * 4;
                    var linha = y * stride;

                    for (var x = 0; x < preview.Largura; x++)
                    {
                        var de = origem + (x * 4);
                        var para = linha + (x * 4);
                        destino[para] = preview.Rgba[de + 2];     // B
                        destino[para + 1] = preview.Rgba[de + 1]; // G
                        destino[para + 2] = preview.Rgba[de];     // R
                        destino[para + 3] = preview.Rgba[de + 3]; // A
                    }
                }

                Marshal.Copy(destino, 0, data.Scan0, destino.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error($"Falha ao ler preview do .blend: {caminho}", ex);
            return null;
        }
    }

    private static byte[]? RenderMagick(string caminho, int size, string ext)
    {
        try
        {
            var settings = new MagickReadSettings
            {
                Density = new Density(144),
            };

            using var image = new MagickImage(caminho, settings);
            image.AutoOrient();

            if (ext.Equals(".exr", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".hdr", StringComparison.OrdinalIgnoreCase))
            {
                // EXR/HDR sao lineares: sem isso a miniatura sai praticamente preta.
                image.AutoLevel();
                image.ColorSpace = ColorSpace.sRGB;
            }

            image.Thumbnail(new MagickGeometry((uint)size, (uint)size) { Greater = true });

            using var ms = new MemoryStream();
            image.Format = MagickFormat.Png;
            image.Write(ms);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error($"Magick falhou em {caminho}", ex);
            return null;
        }
    }

    private async Task<byte[]?> RenderVideoFrameAsync(string caminho, int size, CancellationToken ct)
    {
        var ffmpeg = _ffmpeg.Ffmpeg;
        if (ffmpeg is null)
        {
            return null;
        }

        try
        {
            var psi = new ProcessStartInfo(ffmpeg)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var arg in new[]
                     {
                         "-hide_banner", "-loglevel", "error",
                         "-ss", "1",
                         "-i", caminho,
                         "-frames:v", "1",
                         "-vf", $"scale={size}:-2",
                         "-f", "image2pipe", "-vcodec", "png", "pipe:1",
                     })
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            using var ms = new MemoryStream();
            await process.StandardOutput.BaseStream.CopyToAsync(ms, ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            return process.ExitCode == 0 && ms.Length > 0 ? ms.ToArray() : null;
        }
        catch (Exception ex)
        {
            Log.Error($"Falha ao extrair frame de {caminho}", ex);
            return null;
        }
    }

    private static void Encode(string destino, byte[] png, int size)
    {
        using var image = new MagickImage(png);
        if (image.Width > size || image.Height > size)
        {
            image.Thumbnail(new MagickGeometry((uint)size, (uint)size) { Greater = true });
        }

        image.Format = MagickFormat.WebP;
        image.Quality = 82;

        var tmp = destino + ".tmp";
        image.Write(tmp);
        File.Move(tmp, destino, overwrite: true);
    }
}
