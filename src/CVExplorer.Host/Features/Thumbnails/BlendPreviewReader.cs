using System.Buffers.Binary;

namespace CVExplorer.Host.Features.Thumbnails;

public sealed class BlendPreview
{
    public int Largura { get; init; }

    public int Altura { get; init; }

    /// <summary>Pixels RGBA na ordem do arquivo .blend.</summary>
    public byte[] Rgba { get; init; } = Array.Empty<byte>();
}

/// <summary>
/// Le a miniatura que o Blender grava dentro do proprio .blend (bloco "TEST"):
/// e 128x128 RGBA, gerada ao salvar. Instantaneo, sem abrir o Blender.
/// </summary>
public static class BlendPreviewReader
{
    private static readonly byte[] Magic = "BLENDER"u8.ToArray();

    public static BlendPreview? Read(Stream stream)
    {
        var header = new byte[12];
        if (stream.Read(header, 0, 12) != 12 || !header.AsSpan(0, 7).SequenceEqual(Magic))
        {
            return null;
        }

        var pointerSize = header[7] switch
        {
            (byte)'_' => 4,
            (byte)'-' => 8,
            _ => 8,
        };

        var littleEndian = header[8] != (byte)'V';
        var blockHeaderSize = 4 + 4 + pointerSize + 4 + 4;
        var buffer = new byte[Math.Max(blockHeaderSize, 32)];

        for (var i = 0; i < 512; i++)
        {
            if (!ReadExactly(stream, buffer, blockHeaderSize))
            {
                return null;
            }

            var code = System.Text.Encoding.ASCII.GetString(buffer, 0, 4);
            var size = ReadInt32(buffer.AsSpan(4, 4), littleEndian);

            if (size < 0 || size > 16 * 1024 * 1024)
            {
                return null;
            }

            if (code == "TEST" && size >= 8)
            {
                var data = new byte[size];
                if (!ReadExactly(stream, data, size))
                {
                    return null;
                }

                var width = ReadInt32(data.AsSpan(0, 4), littleEndian);
                var height = ReadInt32(data.AsSpan(4, 4), littleEndian);
                if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
                {
                    return null;
                }

                var pixels = width * height * 4;
                if (data.Length < 8 + pixels)
                {
                    return null;
                }

                return new BlendPreview
                {
                    Largura = width,
                    Altura = height,
                    Rgba = data.AsSpan(8, pixels).ToArray(),
                };
            }

            if (code == "ENDB" || code == "DATA")
            {
                // "DATA" e o primeiro bloco real; se nao achamos TEST antes dele, nao existe.
                if (code == "ENDB")
                {
                    return null;
                }
            }

            if (size > 0 && !Skip(stream, size))
            {
                return null;
            }
        }

        return null;
    }

    private static int ReadInt32(ReadOnlySpan<byte> span, bool littleEndian) =>
        littleEndian ? BinaryPrimitives.ReadInt32LittleEndian(span) : BinaryPrimitives.ReadInt32BigEndian(span);

    private static bool ReadExactly(Stream stream, byte[] buffer, int count)
    {
        var read = 0;
        while (read < count)
        {
            var n = stream.Read(buffer, read, count - read);
            if (n <= 0)
            {
                return false;
            }

            read += n;
        }

        return true;
    }

    private static bool Skip(Stream stream, int count)
    {
        var buffer = new byte[Math.Min(count, 64 * 1024)];
        var remaining = count;
        while (remaining > 0)
        {
            var n = stream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
            if (n <= 0)
            {
                return false;
            }

            remaining -= n;
        }

        return true;
    }
}
