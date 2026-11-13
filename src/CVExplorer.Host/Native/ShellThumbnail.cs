using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CVExplorer.Host.Native;

/// <summary>
/// Miniaturas pelo Shell do Windows: aproveita os handlers ja instalados
/// (Photoshop para PSD, Illustrator para AI, etc.). Roda em threads STA dedicadas
/// porque extensoes de shell podem travar — cada pedido tem timeout.
/// </summary>
internal static class ShellThumbnail
{
    private const int TimeoutMs = 4000;

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [Flags]
    private enum SIIGBF
    {
        BiggerSizeOk = 0x01,
        ThumbnailOnly = 0x08,
        IconOnly = 0x04,
        ScaleUp = 0x100,
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool DrawIconEx(
        IntPtr hdc, int x, int y, IntPtr hIcon, int largura, int altura, uint passo, IntPtr pincel, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint PrivateExtractIcons(
        string arquivo,
        int indice,
        int largura,
        int altura,
        IntPtr[] icones,
        uint[] ids,
        uint quantidade,
        uint flags);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr hObject, int tamanho, ref BITMAP estrutura);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(
        IntPtr hdc, IntPtr hbitmap, uint inicio, uint linhas, byte[] bits, ref BITMAPINFO info, uint uso);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public uint[] bmiColors;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_LARGEICON = 0x0;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    private sealed record Request(string Path, int Size, bool Icone, TaskCompletionSource<byte[]?> Completion);

    private static readonly BlockingCollection<Request> Queue = new();

    static ShellThumbnail()
    {
        for (var i = 0; i < 2; i++)
        {
            var thread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = $"ShellThumb-{i}",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
    }

    public static Task<byte[]?> GetPngAsync(string path, int size, CancellationToken ct) =>
        Enfileirar(path, size, icone: false, ct);

    /// <summary>
    /// Icone do item como o Explorer mostra (o do proprio .exe/.lnk quando existe), no
    /// maior tamanho que o Shell tiver — normalmente 256 px — e com transparencia.
    /// </summary>
    public static Task<byte[]?> GetIconPngAsync(string path, int size, CancellationToken ct) =>
        Enfileirar(path, size, icone: true, ct);

    private static async Task<byte[]?> Enfileirar(string path, int size, bool icone, CancellationToken ct)
    {
        var completion = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            Queue.Add(new Request(path, size, icone, completion), ct);
        }
        catch (Exception)
        {
            return null;
        }

        var finished = await Task.WhenAny(completion.Task, Task.Delay(TimeoutMs, ct)).ConfigureAwait(false);
        return finished == completion.Task ? await completion.Task.ConfigureAwait(false) : null;
    }

    private static void WorkerLoop()
    {
        foreach (var request in Queue.GetConsumingEnumerable())
        {
            try
            {
                request.Completion.TrySetResult(
                    request.Icone ? RenderIcon(request.Path, request.Size) : Render(request.Path, request.Size));
            }
            catch (COMException)
            {
                // 0x8004B200 (sem miniatura/icone neste formato) e o caminho normal:
                // a cadeia de provedores assume a partir daqui.
                request.Completion.TrySetResult(null);
            }
            catch (Exception ex)
            {
                Log.Error($"Falha ao obter imagem do Shell: {request.Path}", ex);
                request.Completion.TrySetResult(null);
            }
        }
    }

    private static byte[]? Render(string path, int size)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory) != 0)
        {
            return null;
        }

        var bitmap = IntPtr.Zero;
        try
        {
            ((IShellItemImageFactory)factory).GetImage(
                new SIZE { cx = size, cy = size },
                SIIGBF.ThumbnailOnly | SIIGBF.BiggerSizeOk,
                out bitmap);

            if (bitmap == IntPtr.Zero)
            {
                return null;
            }

            using var image = Image.FromHbitmap(bitmap);
            using var stream = new MemoryStream();
            image.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        finally
        {
            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }

            if (factory is not null && Marshal.IsComObject(factory))
            {
                Marshal.ReleaseComObject(factory);
            }
        }
    }

    /// <summary>
    /// Icone do item pelo Shell, no maior tamanho disponivel (256 px quando o arquivo tem
    /// essa resolucao) e preservando o alfa — e o mesmo desenho que o Explorer mostra,
    /// inclusive o icone proprio de cada .exe/.lnk.
    /// </summary>
    private static byte[]? RenderIcon(string path, int size)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory) == 0
            && factory is IShellItemImageFactory fabrica)
        {
            var bitmap = IntPtr.Zero;
            try
            {
                var hr = fabrica.GetImage(
                    new SIZE { cx = size, cy = size },
                    SIIGBF.IconOnly | SIIGBF.BiggerSizeOk | SIIGBF.ScaleUp,
                    out bitmap);

                if (hr == 0 && bitmap != IntPtr.Zero)
                {
                    var png = PngFromHBitmap(bitmap);
                    if (png is not null)
                    {
                        return png;
                    }
                }
            }
            finally
            {
                if (bitmap != IntPtr.Zero)
                {
                    DeleteObject(bitmap);
                }

                if (Marshal.IsComObject(factory))
                {
                    Marshal.ReleaseComObject(factory);
                }
            }
        }

        // 2) Icone lido direto do arquivo (PE, .ico, .lnk): o mesmo caminho que o Explorer
        // usa e que continua valendo quando o handler do Shell devolve o icone do tipo.
        var extraido = PngFromPrivateExtract(path);
        if (extraido is not null)
        {
            return extraido;
        }

        // 3) Caminho virtual (unidade de rede sem handler, item de compactado): icone do
        // tipo em 32 px pelo SHGetFileInfo, que e o que existe sem instalar nada.
        return PngFromHIcon(path, size);
    }

    /// <summary>Icone de dentro do arquivo (grupo de icones do PE), no maior tamanho.</summary>
    private static byte[]? PngFromPrivateExtract(string path)
    {
        foreach (var lado in new[] { 256, 64, 48, 32 })
        {
            var icones = new IntPtr[1];
            var ids = new uint[1];
            var total = PrivateExtractIcons(path, 0, lado, lado, icones, ids, 1, 0);

            if (total == 0 || icones[0] == IntPtr.Zero)
            {
                continue;
            }

            try
            {
                using var bitmap = new Bitmap(lado, lado, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.Clear(Color.Transparent);
                    var hdc = graphics.GetHdc();
                    try
                    {
                        DrawIconEx(hdc, 0, 0, icones[0], lado, lado, 0, IntPtr.Zero, 0x0003 /* DI_NORMAL */);
                    }
                    finally
                    {
                        graphics.ReleaseHdc(hdc);
                    }
                }

                using var stream = new MemoryStream();
                bitmap.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
            finally
            {
                DestroyIcon(icones[0]);
            }
        }

        return null;
    }

    private static byte[]? PngFromHIcon(string path, int size)
    {
        var info = new SHFILEINFO();
        var flags = SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES;
        var result = SHGetFileInfo(path, FILE_ATTRIBUTE_NORMAL, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.Clear(Color.Transparent);
                var hdc = graphics.GetHdc();
                try
                {
                    DrawIconEx(hdc, 0, 0, info.hIcon, 32, 32, 0, IntPtr.Zero, 0x0003 /* DI_NORMAL */);
                }
                finally
                {
                    graphics.ReleaseHdc(hdc);
                }
            }

            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    /// <summary>
    /// HBITMAP do Shell (32 bpp, fundo transparente) para PNG. Image.FromHbitmap jogaria
    /// o alfa fora — e o icone sairia com fundo preto —, entao os pixels vem por GetDIBits.
    /// </summary>
    private static byte[]? PngFromHBitmap(IntPtr hbitmap)
    {
        var medidas = new BITMAP();
        if (GetObject(hbitmap, Marshal.SizeOf<BITMAP>(), ref medidas) == 0)
        {
            return null;
        }

        var largura = medidas.bmWidth;
        var altura = medidas.bmHeight;
        if (largura <= 0 || altura <= 0 || largura > 4096 || altura > 4096)
        {
            return null;
        }

        var info = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = largura,
                biHeight = -altura,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
            },
        };

        var pixels = new byte[largura * altura * 4];
        var tela = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(tela, hbitmap, 0, (uint)altura, pixels, ref info, 0) == 0)
            {
                return null;
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, tela);
        }

        // Icone antigo (sem canal alfa) chega com tudo zero: sem isso ficaria invisivel.
        var temAlfa = false;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0)
            {
                temAlfa = true;
                break;
            }
        }

        if (!temAlfa)
        {
            for (var i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;
            }
        }

        using var bitmap = new Bitmap(largura, altura, PixelFormat.Format32bppArgb);
        var dados = bitmap.LockBits(
            new Rectangle(0, 0, largura, altura),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            Marshal.Copy(pixels, 0, dados.Scan0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(dados);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
