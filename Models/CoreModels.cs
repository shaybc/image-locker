namespace ImageLocker.Models;

/// <summary>
/// Represents one GA candidate solution.
/// The gene array stores the logical RGB channel positions selected for payload embedding.
/// </summary>
public class Chromosome
{
    public int[] Genes { get; }
    public double Psnr { get; set; }
    public int ChangesCount { get; set; }

    /// <summary>
    /// Creates a chromosome from a precomputed list of embedding positions.
    /// </summary>
    /// <param name="genes">Sorted logical RGB channel indices chosen for payload embedding.</param>

    public Chromosome(int[] genes)
    {
        ArgumentNullException.ThrowIfNull(genes);
        this.Genes = genes;
    }

    /// <summary>
    /// Creates a deep copy of the chromosome so it can be reused or modified safely.
    /// </summary>
    /// <returns>A new chromosome instance with copied genes and score values.</returns>

    public Chromosome Clone()
    {
        return new Chromosome((int[])Genes.Clone())
        {
            Psnr = this.Psnr,
            ChangesCount = this.ChangesCount
        };
    }
}

/// <summary>
/// Holds the tunable parameters used by the genetic algorithm.
/// </summary>
public class GaParameters
{
    public int PopulationSize { get; set; } = 8;
    public int Generations { get; set; } = 4;
    public double MutationRate { get; set; } = 0.10;

    /// <summary>
    /// Checks that all GA parameters are inside allowed ranges before optimization starts.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when one of the GA values is outside its allowed range.</exception>

    public void Validate()
    {
        if (PopulationSize < 2)
        {
            throw new InvalidOperationException("Population size must be at least 2.");
        }

        if (Generations < 1)
        {
            throw new InvalidOperationException("Generations must be at least 1.");
        }

        if (MutationRate < 0 || MutationRate > 1)
        {
            throw new InvalidOperationException("Mutation rate must be in the range [0, 1].");
        }
    }
}

/// <summary>
/// Stores the binary header fields needed to extract the hidden image correctly.
/// </summary>
public class HeaderInfo
{
    public int PayloadWidth { get; init; }
    public int PayloadHeight { get; init; }
    public int PayloadByteLength { get; init; }
    public int MetadataByteLength { get; init; }
    public uint MetadataCrc32 { get; init; }
    public uint PayloadCrc32 { get; init; }
}

/// <summary>
/// Represents a loaded PNG image in memory using BGRA32 pixel storage.
/// </summary>
public class PngImageData
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public int Stride => Width * 4;
    public int PixelCount => Width * Height;
    public int RgbChannelCount => PixelCount * 3;

    /// <summary>
    /// Creates an in-memory PNG image wrapper and verifies that the pixel buffer size matches width, height, and BGRA32 format.
    /// </summary>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="pixels">Raw BGRA32 pixel buffer.</param>

    public PngImageData(int width, int height, byte[] pixels)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        ArgumentNullException.ThrowIfNull(pixels);

        var expectedLength = width * height * 4;
        if (pixels.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Pixel buffer length must be {expectedLength} bytes for BGRA32, but got {pixels.Length}.",
                nameof(pixels));
        }

        this.Width = width;
        this.Height = height;
        this.Pixels = pixels;
    }
}
