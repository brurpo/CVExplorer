using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ImageMagick;
using CVExplorer.Host.Features.Media;
using CVExplorer.Host.Native;

namespace CVExplorer.Host.Features.Thumbnails;

public sealed class ThumbnailResult
{
    public string Chave { get; set; } = string.Empty;

    public bool Pronta { get; set; }
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

    private readonly AppSettings _settings;
    private readonly FfmpegLocator _ffmpeg;
    private readonly SemaphoreSlim _limite = new(Math.Clamp(Environment.ProcessorCount / 2, 2, 6));
    private readonly ConcurrentDictionary<string, Task<ThumbnailResult>> _emAndamento = new();

    public ThumbnailService(AppSettings settings, FfmpegLocator ffmpeg)
    {
        _settings = settings;
        _ffmpeg = ffmpeg;
        Directory.CreateDirectory(AppPaths.ThumbsDir);
    }

    public static string UrlFor(string chave) =>
        $"https://{AppPaths.ThumbsVirtualHost}/{chave}.webp";

    public static string Chave(string caminho, long tamanho, DateTime modificado, int size)
    {
        var raw = $"{caminho.ToLowerInvariant()}|{tamanho}|{modificado.Ticks}|{size}";
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

        var task = _emAndamento.GetOrAdd(chave, _ => GenerateAsync(caminho, destino, size, chave, ct));
        try
        {
            return await task.ConfigureAwait(false);
        }
        finally
        {
            if (task.IsCompleted)
            {
                _emAndamento.TryRemove(chave, out _);
            }
        }
    }

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

        // 1) Shell: melhor resultado para formatos com handler instalado (PSD/AI/PDF).
        var shell = await ShellThumbnail.GetPngAsync(caminho, size, ct).ConfigureAwait(false);
        if (shell is not null)
        {
            return shell;
        }

        // 2) .blend: preview embutido, sem depender do Blender.
        if (ext.Equals(".blend", StringComparison.OrdinalIgnoreCase))
        {
            var preview = await Task.Run(() => ReadBlend(caminho), ct).ConfigureAwait(false);
            if (preview is not null)
            {
                return preview;
            }
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
                // O .blend guarda o preview de baixo para cima (padrao de imagem do Blender).
                var stride = data.Stride;
                var destino = new byte[stride * preview.Altura];
                for (var y = 0; y < preview.Altura; y++)
                {
                    var src = (preview.Altura - 1 - y) * preview.Largura * 4;
                    Buffer.BlockCopy(preview.Rgba, src, destino, y * stride, preview.Largura * 4);
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
