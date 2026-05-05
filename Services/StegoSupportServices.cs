using System.Text;
using System.Security.Cryptography;
using ImageLocker.Models;

namespace ImageLocker.Services;

/// <summary>
/// Builds and parses the fixed-size binary header stored at the beginning of the stego image.
/// </summary>
public class BinaryHeaderService
{
    // Header size 6 ints * 4 bytes = 24 bytes (stores width, height, 2 lengths, 2 CRC32).
    public const int HeaderSizeBytes = 24;

    /// <summary>
    /// Builds the 24-byte header that stores payload dimensions, lengths, and CRC32 values.
    /// </summary>
    /// <param name="payloadWidth">Hidden image width.</param>
    /// <param name="payloadHeight">Hidden image height.</param>
    /// <param name="payloadByteLength">Hidden PNG size in bytes.</param>
    /// <param name="metadataByteLength">Compressed embedding-map size in bytes.</param>
    /// <param name="metadataCrc32">CRC32 of the stored metadata bytes.</param>
    /// <param name="payloadCrc32">CRC32 of the original payload bytes.</param>
    /// <returns>The packed 24-byte header.</returns>

    public byte[] BuildHeader(
        int payloadWidth,
        int payloadHeight,
        int payloadByteLength,
        int metadataByteLength,
        uint metadataCrc32,
        uint payloadCrc32)
    {
        if (payloadWidth <= 0) throw new ArgumentOutOfRangeException(nameof(payloadWidth));
        if (payloadHeight <= 0) throw new ArgumentOutOfRangeException(nameof(payloadHeight));
        if (payloadByteLength <= 0) throw new ArgumentOutOfRangeException(nameof(payloadByteLength));
        if (metadataByteLength <= 0) throw new ArgumentOutOfRangeException(nameof(metadataByteLength));

        var header = new byte[HeaderSizeBytes];
        CopyIntToBuffer(payloadWidth, header, 0);
        CopyIntToBuffer(payloadHeight, header, 4);
        CopyIntToBuffer(payloadByteLength, header, 8);
        CopyIntToBuffer(metadataByteLength, header, 12);
        CopyUIntToBuffer(metadataCrc32, header, 16);
        CopyUIntToBuffer(payloadCrc32, header, 20);
        return header;
    }

    /// <summary>
    /// Parses the fixed binary header back into a strongly typed HeaderInfo object.
    /// </summary>
    /// <param name="header">Raw 24-byte header buffer.</param>
    /// <returns>A parsed <see cref="HeaderInfo"/> instance.</returns>

    public HeaderInfo ParseHeader(byte[] header)
    {
        ArgumentNullException.ThrowIfNull(header);
        if (header.Length != HeaderSizeBytes)
        {
            throw new InvalidOperationException($"Header must be exactly {HeaderSizeBytes} bytes.");
        }

        var width = ReadIntFromBuffer(header, 0);
        var height = ReadIntFromBuffer(header, 4);
        var byteLength = ReadIntFromBuffer(header, 8);
        var metadataByteLength = ReadIntFromBuffer(header, 12);
        var metadataCrc32 = ReadUIntFromBuffer(header, 16);
        var payloadCrc32 = ReadUIntFromBuffer(header, 20);

        if (width <= 0 || height <= 0 || byteLength <= 0 || metadataByteLength <= 0)
        {
            throw new InvalidOperationException("Header contains invalid dimensions or lengths.");
        }

        return new HeaderInfo
        {
            PayloadWidth = width,
            PayloadHeight = height,
            PayloadByteLength = byteLength,
            MetadataByteLength = metadataByteLength,
            MetadataCrc32 = metadataCrc32,
            PayloadCrc32 = payloadCrc32
        };
    }

    /// <summary>
    /// Writes a 32-bit integer into a byte buffer at the requested offset.
    /// </summary>

    private static void CopyIntToBuffer(int value, byte[] buffer, int offset)
    {
        var bytes = BitConverter.GetBytes(value);
        Array.Copy(bytes, 0, buffer, offset, bytes.Length);
    }

    /// <summary>
    /// Writes a 32-bit unsigned integer into a byte buffer at the requested offset.
    /// </summary>

    private static void CopyUIntToBuffer(uint value, byte[] buffer, int offset)
    {
        var bytes = BitConverter.GetBytes(value);
        Array.Copy(bytes, 0, buffer, offset, bytes.Length);
    }

    /// <summary>
    /// Reads a 32-bit integer from a byte buffer.
    /// </summary>

    private static int ReadIntFromBuffer(byte[] buffer, int offset)
    {
        return BitConverter.ToInt32(buffer, offset);
    }

    /// <summary>
    /// Reads a 32-bit unsigned integer from a byte buffer.
    /// </summary>

    private static uint ReadUIntFromBuffer(byte[] buffer, int offset)
    {
        return BitConverter.ToUInt32(buffer, offset);
    }
}

/// <summary>
/// Converts between byte arrays and bit arrays.
/// This is required because LSB embedding works one bit at a time.
/// </summary>
public class BitStreamService
{

    /// <summary>
    /// Converts bytes into a big-endian (MSB first) bit sequence suitable for sequential embedding.
    /// </summary>
    /// <param name="bytes">Input bytes to convert.</param>
    /// <returns>An array containing 0/1 bit values.</returns>
    public int[] ToBits(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var bits = new int[bytes.Length * 8];
        var cursor = 0;

        foreach (var value in bytes)
        {
            for (var bit = 7; bit >= 0; bit--)
            {
                bits[cursor++] = (value >> bit) & 1;
            }
        }

        return bits;
    }

    /// <summary>
    /// Converts a bit sequence back into bytes.
    /// Used during extraction after header, metadata, or payload bits are read.
    /// </summary>
    /// <param name="bits">Input bit sequence. The count must be divisible by 8.</param>
    /// <returns>A byte array reconstructed from the bit sequence.</returns>

    public byte[] ToBytes(IReadOnlyList<int> bits)
    {
        ArgumentNullException.ThrowIfNull(bits);

        if (bits.Count % 8 != 0)
        {
            throw new InvalidOperationException("Bit count must be a multiple of 8.");
        }

        var bytes = new byte[bits.Count / 8];

        for (var i = 0; i < bytes.Length; i++)
        {
            byte value = 0;
            for (var bit = 0; bit < 8; bit++)
            {
                var current = bits[(i * 8) + bit];
                if (current != 0 && current != 1)
                {
                    throw new InvalidOperationException("Bits must contain only 0 or 1.");
                }

                value = (byte)((value << 1) | current);
            }

            bytes[i] = value;
        }

        return bytes;
    }
}

/// <summary>
/// Converts a logical RGB channel index into its real byte offset inside the BGRA32 pixel buffer.
/// </summary>
public static class ChannelIndexHelper
{

    /// <summary>
    /// Maps a logical RGB index to a byte offset while skipping alpha bytes (transparency pixel 0-255).
    /// </summary>
    /// <param name="logicalRgbChannelIndex">Zero-based RGB channel index that ignores alpha bytes.</param>
    /// <returns>The matching byte offset inside the BGRA32 buffer.</returns>
    public static int ToByteOffset(int logicalRgbChannelIndex)
    {
        if (logicalRgbChannelIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(logicalRgbChannelIndex));
        }

        var pixelIndex = logicalRgbChannelIndex / 3;
        var channelInPixel = logicalRgbChannelIndex % 3;
        var pixelBase = pixelIndex * 4;

        return channelInPixel switch
        {
            0 => pixelBase + 2, // R
            1 => pixelBase + 1, // G
            _ => pixelBase + 0  // B
        };
    }
}

/// <summary>
/// Computes CRC32 checksums for metadata and payload validation.
/// </summary>
public class Crc32Service
{
    private static readonly uint[] Table = BuildTable();

    /// <summary>
    /// Computes the CRC32 checksum for a byte array.
    /// </summary>
    /// <param name="data">Input data to hash.</param>
    /// <returns>The CRC32 checksum.</returns>

    public uint Compute(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        uint crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc = (crc >> 8) ^ Table[(crc ^ value) & 0xFF];
        }

        return ~crc;
    }

    /// <summary>
    /// Builds the lookup table used by the CRC32 implementation.
    /// This is done once and reused by all checksum calculations.
    /// </summary>
    /// <returns>The 256-entry CRC32 lookup table.</returns>

    private static uint[] BuildTable()
    {
        var table = new uint[256];

        // Standard reflected CRC32 polynomial used to build the lookup table once.
        const uint polynomial = 0xEDB88320u;

        for (uint i = 0; i < table.Length; i++)
        {
            uint c = i;
            for (var j = 0; j < 8; j++)
            {
                c = (c & 1) == 1 ? polynomial ^ (c >> 1) : c >> 1;
            }

            table[i] = c;
        }

        return table;
    }
}

/// <summary>
/// Applies the XOR cipher used by the project for lightweight encryption and decryption.
/// </summary>
public class XorCipherService
{

    /// <summary>
    /// Applies XOR to the input data.
    /// The same method is used for both encryption and decryption because XOR is symmetric.
    /// </summary>
    /// <param name="data">Input bytes to encrypt or decrypt.</param>
    /// <param name="key">XOR key text. Null or empty means no encryption.</param>
    /// <returns>A new byte array containing the XOR result.</returns>
    public byte[] Apply(byte[] data, string? key)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (string.IsNullOrEmpty(key))
        {
            return (byte[])data.Clone();
        }

        var keyBytes = this.BuildDerivedKeyBytes(key);
        var output = new byte[data.Length];

        for (var i = 0; i < data.Length; i++)
        {
            output[i] = (byte)(data[i] ^ keyBytes[i % keyBytes.Length]);
        }

        return output;
    }

    /// <summary>
    /// Converts the full key text into a fixed byte sequence, ensuring different effective keys for similar inputs.
    /// </summary>
    /// <param name="key">User key text.</param>
    /// <returns>Derived key bytes based on the full text.</returns>
    private byte[] BuildDerivedKeyBytes(string key)
    {
        var sourceBytes = Encoding.UTF8.GetBytes(key);

        // Creating a Unique XOR pattern from the full key text, preventing collisions like "a" and "aaaaaa".
        return SHA256.HashData(sourceBytes);
    }
}
