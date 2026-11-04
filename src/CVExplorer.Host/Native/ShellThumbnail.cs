using System.Collections.Concurrent;
using System.Drawing;
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
        ScaleUp = 0x100,
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
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

    private sealed record Request(string Path, int Size, TaskCompletionSource<byte[]?> Completion);

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

    public static async Task<byte[]?> GetPngAsync(string path, int size, CancellationToken ct)
    {
        var completion = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            Queue.Add(new Request(path, size, completion), ct);
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
                request.Completion.TrySetResult(Render(request.Path, request.Size));
            }
            catch (Exception)
            {
                // Sem handler instalado para o formato (ex.: .blend, .svg, .exr):
                // e um caminho esperado, a cadeia de provedores assume a partir daqui.
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

    /// <summary>Icone associado ao tipo do arquivo — ultimo recurso da cadeia.</summary>
    public static byte[]? GetTypeIcon(string path)
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
            using var icon = Icon.FromHandle(info.hIcon);
            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }
}
