using System.Runtime.InteropServices;

namespace CVExplorer.Host.Native;

internal static class Win32
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Ponto { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Ponto ponto);

    /// <summary>Posicao do cursor em pixels fisicos da tela (independe da escala).</summary>
    public static bool CursorPos(out int x, out int y)
    {
        if (GetCursorPos(out var ponto))
        {
            x = ponto.X;
            y = ponto.Y;
            return true;
        }

        x = 0;
        y = 0;
        return false;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetDiskFreeSpaceEx(
        string lpDirectoryName,
        out ulong lpFreeBytesAvailableToCaller,
        out ulong lpTotalNumberOfBytes,
        out ulong lpTotalNumberOfFreeBytes);

    /// <summary>Espaco livre em qualquer caminho, inclusive UNC (DriveInfo nao cobre bem).</summary>
    public static (long Livre, long Total) FreeSpace(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
            {
                root = path;
            }

            if (GetDiskFreeSpaceEx(root, out var livre, out var total, out _))
            {
                return ((long)livre, (long)total);
            }
        }
        catch
        {
            // silencioso: a UI so deixa de mostrar o espaco livre
        }

        return (0, 0);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr[]? apidl, uint dwFlags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr ptr);

    /// <summary>Abre o Explorer com o item selecionado.</summary>
    public static bool RevealInExplorer(string path)
    {
        IntPtr pidl = IntPtr.Zero;
        try
        {
            if (SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out _) != 0 || pidl == IntPtr.Zero)
            {
                return false;
            }

            return SHOpenFolderAndSelectItems(pidl, 0, null, 0) == 0;
        }
        catch (Exception ex)
        {
            Log.Error($"Falha ao revelar {path}", ex);
            return false;
        }
        finally
        {
            if (pidl != IntPtr.Zero)
            {
                CoTaskMemFree(pidl);
            }
        }
    }

    /// <summary>Prefixo \\?\ para caminhos acima de MAX_PATH.</summary>
    public static string LongPath(string path)
    {
        if (path.Length < 250 || path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return path;
        }

        return path.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC\" + path.Substring(2)
            : @"\\?\" + path;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>Traz a janela ja aberta para frente (segunda execucao do app).</summary>
    public static void TrazerParaFrente(string titulo)
    {
        var janela = FindWindow(null, titulo);
        if (janela == IntPtr.Zero)
        {
            return;
        }

        ShowWindow(janela, 9); // SW_RESTORE
        SetForegroundWindow(janela);
    }
}
