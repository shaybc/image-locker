using System.Diagnostics;
using System.IO;
using ImageLocker.Models;

namespace ImageLocker.Services;

/// <summary>
/// Main coordination layer of the project.
/// It connects image I/O, XOR, header and metadata packing, LSB access, CRC32 checks, GA optimization, and quality metrics.
/// </summary>
public class SteganographyCoordinator
{
    // reserves 12% extra metadata space for buffer safety.
    private const double MetadataSafetyFactor = 1.12;

    // PNG signature for validating the data as a PNG.
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    private readonly ImageIOService imageIoService;
    private readonly BinaryHeaderService headerService;
    private readonly BitStreamService bitStreamService;
    private readonly XorCipherService xorCipherService;
    private readonly LsbSteganographyService lsbService;
    private readonly MetricsService metricsService;
    private readonly GeneticAlgorithmService gaService;
    private readonly Crc32Service crc32Service;

    /// <summary>
    /// Creates the coordinator and receives all lower-level services through dependency injection.
    /// </summary>
    /// <param name="imageIOService">PNG loading and saving service.</param>
    /// <param name="headerService">Binary header packing service.</param>
    /// <param name="bitStreamService">Byte/bit conversion service.</param>
    /// <param name="xorCipherService">XOR encryption service.</param>
    /// <param name="lsbService">Low-level LSB read/write service.</param>
    /// <param name="metricsService">Image quality metrics service.</param>
    /// <param name="gaService">Genetic algorithm optimizer.</param>
    /// <param name="crc32Service">CRC32 validation service.</param>

    public SteganographyCoordinator(
        ImageIOService imageIOService,
        BinaryHeaderService headerService,
        BitStreamService bitStreamService,
        XorCipherService xorCipherService,
        LsbSteganographyService lsbService,
        MetricsService metricsService,
        GeneticAlgorithmService gaService,
        Crc32Service crc32Service)
    {
        this.imageIoService = imageIOService;
        this.headerService = headerService;
        this.bitStreamService = bitStreamService;
        this.xorCipherService = xorCipherService;
        this.lsbService = lsbService;
        this.metricsService = metricsService;
        this.gaService = gaService;
        this.crc32Service = crc32Service;
    }

    /// <summary>
    /// Runs the full embed pipeline.
    /// It loads images, encrypts the payload, reserves the prefix area, optimizes payload positions, writes header and metadata, embeds the payload, saves the stego image, and returns operation statistics.
    /// </summary>
    /// <param name="coverPath">Path of the cover PNG.</param>
    /// <param name="payloadPath">Path of the hidden PNG.</param>
    /// <param name="stegoOutputPath">Output path of the generated stego PNG.</param>
    /// <param name="key">Optional XOR key.</param>
    /// <param name="parameters">GA settings used to search embedding positions.</param>
    /// <param name="progress">Optional progress sink for UI updates.</param>
    /// <returns>An <see cref="EmbedResult"/> containing output statistics.</returns>

    public EmbedResult Embed(
        string coverPath,
        string payloadPath,
        string stegoOutputPath,
        string? key,
        GaParameters parameters,
        IProgress<OperationProgress>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(coverPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(stegoOutputPath);
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();

        var stopwatch = Stopwatch.StartNew();
        Report(progress, "Prepare", 0, 1, 0, stopwatch.Elapsed, TimeSpan.Zero, "Loading images and building packet.");

        var cover = this.imageIoService.LoadPng(coverPath);
        var payloadMetadata = this.imageIoService.LoadPng(payloadPath);
        var payloadBytes = File.ReadAllBytes(payloadPath);
        ValidatePngSignature(payloadBytes);

        var payloadEncrypted = this.xorCipherService.Apply(payloadBytes, key);
        var payloadBits = this.bitStreamService.ToBits(payloadEncrypted);
        var headerBitsCount = BinaryHeaderService.HeaderSizeBytes * 8;
        var rgbChannelCount = cover.RgbChannelCount;

        var estimatedMetadataByteLength = EstimateMetadataByteLength(rgbChannelCount, payloadBits.Length);
        var prefixBitsCount = checked(headerBitsCount + (estimatedMetadataByteLength * 8));
        EnsureCapacity(rgbChannelCount, prefixBitsCount, payloadBits.Length);

        Report(progress, "Genetic algorithm", 0, parameters.Generations, 5, stopwatch.Elapsed, TimeSpan.Zero, $"Selecting embedding positions (estimated metadata {estimatedMetadataByteLength} bytes).");
        var best = this.gaService.Optimize(
            cover.Pixels,
            payloadBits,
            prefixBitsCount,
            rgbChannelCount,
            parameters,
            (completed, total, elapsed, remaining) =>
            {
                var overallPercent = 5.0 + (completed / (double)total) * 75.0;
                Report(progress, "Genetic algorithm", completed, total, overallPercent, elapsed, remaining, $"Evaluated generation {completed} of {total}.");
            });

        var reservedMetadataByteLength = estimatedMetadataByteLength;
        var metadataBytes = SerializeGenesCompressed(best.Genes, prefixBitsCount);
        if (metadataBytes.Length > reservedMetadataByteLength)
        {
            var shiftBits = checked((metadataBytes.Length - reservedMetadataByteLength) * 8);
            var shiftedGenes = ShiftGenes(best.Genes, shiftBits, rgbChannelCount);
            var shiftedPrefixBitsCount = checked(prefixBitsCount + shiftBits);
            EnsureCapacity(rgbChannelCount, shiftedPrefixBitsCount, payloadBits.Length);

            best = new Chromosome(shiftedGenes)
            {
                Psnr = best.Psnr,
                ChangesCount = best.ChangesCount
            };

            prefixBitsCount = shiftedPrefixBitsCount;
            reservedMetadataByteLength = metadataBytes.Length;
            metadataBytes = SerializeGenesCompressed(best.Genes, prefixBitsCount);

            if (metadataBytes.Length > reservedMetadataByteLength)
            {
                throw new InvalidOperationException("Embedding metadata grew unexpectedly after prefix alignment.");
            }
        }

        metadataBytes = PadMetadata(metadataBytes, reservedMetadataByteLength);

        var metadataCrc32 = this.crc32Service.Compute(metadataBytes);
        var payloadCrc32 = this.crc32Service.Compute(payloadBytes);

        var headerBytes = this.headerService.BuildHeader(
            payloadMetadata.Width,
            payloadMetadata.Height,
            payloadBytes.Length,
            reservedMetadataByteLength,
            metadataCrc32,
            payloadCrc32);

        var encryptedHeaderBytes = this.xorCipherService.Apply(headerBytes, key);
        var encryptedMetadataBytes = this.xorCipherService.Apply(metadataBytes, key);

        var encryptedHeaderBits = this.bitStreamService.ToBits(encryptedHeaderBytes);
        var encryptedMetadataBits = this.bitStreamService.ToBits(encryptedMetadataBytes);

        var stegoPixels = (byte[])cover.Pixels.Clone();

        Report(progress, "Header and metadata", 1, 1, 85, stopwatch.Elapsed, TimeSpan.Zero, "Embedding encrypted header and metadata.");
        WriteSequentialBits(stegoPixels, 0, encryptedHeaderBits);
        WriteSequentialBits(stegoPixels, encryptedHeaderBits.Length, encryptedMetadataBits);

        Report(progress, "Payload", 1, 1, 92, stopwatch.Elapsed, TimeSpan.Zero, "Embedding encrypted payload.");
        for (var i = 0; i < payloadBits.Length; i++)
        {
            this.lsbService.WriteBit(stegoPixels, best.Genes[i], payloadBits[i]);
        }

        var stego = new PngImageData(cover.Width, cover.Height, stegoPixels);
        this.imageIoService.SavePng(stego, stegoOutputPath);

        Report(progress, "Complete", 1, 1, 100, stopwatch.Elapsed, TimeSpan.Zero, "Embedding completed.");

        return new EmbedResult
        {
            OutputPath = stegoOutputPath,
            HeaderBits = encryptedHeaderBits.Length,
            MetadataBits = encryptedMetadataBits.Length,
            PayloadBits = payloadBits.Length,
            ChangedChannels = CountChangedChannels(cover.Pixels, stegoPixels),
            Mse = this.metricsService.ComputeMseRgb(cover.Pixels, stegoPixels),
            Psnr = this.metricsService.ComputePsnrRgb(cover.Pixels, stegoPixels)
        };
    }

    /// <summary>
    /// Runs the full extract pipeline.
    /// It reads and decrypts the header and metadata, reconstructs the embedding positions, extracts the payload bits, validates CRC32 and PNG signature, and saves the recovered file.
    /// </summary>
    /// <param name="stegoPath">Path of the stego PNG.</param>
    /// <param name="payloadOutputPath">Path where the recovered payload PNG will be saved.</param>
    /// <param name="key">Optional XOR key used during embedding.</param>
    /// <param name="progress">Optional progress sink for UI updates.</param>
    /// <returns>An <see cref="ExtractionResult"/> describing the recovered payload.</returns>

    public ExtractionResult Extract(string stegoPath, string payloadOutputPath, string? key, IProgress<OperationProgress>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stegoPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadOutputPath);

        var stopwatch = Stopwatch.StartNew();
        Report(progress, "Prepare", 0, 1, 0, stopwatch.Elapsed, TimeSpan.Zero, "Loading stego image.");

        var stego = this.imageIoService.LoadPng(stegoPath);
        var rgbChannelCount = stego.RgbChannelCount;

        var encryptedHeaderBitsCount = BinaryHeaderService.HeaderSizeBytes * 8;
        if (rgbChannelCount < encryptedHeaderBitsCount)
        {
            throw new InvalidOperationException("Stego image does not contain enough capacity to store the fixed header.");
        }

        Report(progress, "Header", 1, 4, 15, stopwatch.Elapsed, TimeSpan.Zero, "Reading and decrypting header.");
        var encryptedHeaderBits = ReadSequentialBits(stego.Pixels, 0, encryptedHeaderBitsCount);
        var encryptedHeaderBytes = this.bitStreamService.ToBytes(encryptedHeaderBits);
        var headerBytes = this.xorCipherService.Apply(encryptedHeaderBytes, key);
        var header = this.headerService.ParseHeader(headerBytes);
        ValidateHeader(header, rgbChannelCount);

        var encryptedMetadataBitsCount = checked(header.MetadataByteLength * 8);
        var prefixBitsCount = checked(encryptedHeaderBitsCount + encryptedMetadataBitsCount);

        Report(progress, "Metadata", 2, 4, 40, stopwatch.Elapsed, TimeSpan.Zero, "Reading and decrypting metadata.");
        var encryptedMetadataBits = ReadSequentialBits(stego.Pixels, encryptedHeaderBitsCount, encryptedMetadataBitsCount);
        var encryptedMetadataBytes = this.bitStreamService.ToBytes(encryptedMetadataBits);
        var metadataBytes = this.xorCipherService.Apply(encryptedMetadataBytes, key);
        var actualMetadataCrc32 = this.crc32Service.Compute(metadataBytes);
        if (actualMetadataCrc32 != header.MetadataCrc32)
        {
            throw new InvalidOperationException("Metadata CRC32 validation failed. The key may be incorrect or the stego data is corrupted.");
        }

        var genes = DeserializeGenesCompressed(metadataBytes, checked(header.PayloadByteLength * 8), prefixBitsCount);
        var payloadBitsCount = checked(header.PayloadByteLength * 8);
        ValidateGenes(genes, prefixBitsCount, rgbChannelCount);

        Report(progress, "Payload", 3, 4, 70, stopwatch.Elapsed, TimeSpan.Zero, "Extracting encrypted payload bits.");
        var payloadBits = new int[payloadBitsCount];
        for (var i = 0; i < payloadBitsCount; i++)
        {
            payloadBits[i] = this.lsbService.ReadBit(stego.Pixels, genes[i]);
        }

        var payloadEncrypted = this.bitStreamService.ToBytes(payloadBits);
        var payloadBytes = this.xorCipherService.Apply(payloadEncrypted, key);
        var actualPayloadCrc32 = this.crc32Service.Compute(payloadBytes);
        if (actualPayloadCrc32 != header.PayloadCrc32)
        {
            throw new InvalidOperationException("Payload CRC32 validation failed. The key may be incorrect or the extracted data is corrupted.");
        }

        ValidatePngSignature(payloadBytes);

        var directory = Path.GetDirectoryName(payloadOutputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(payloadOutputPath, payloadBytes);
        Report(progress, "Complete", 4, 4, 100, stopwatch.Elapsed, TimeSpan.Zero, "Extraction completed.");

        return new ExtractionResult
        {
            OutputPath = payloadOutputPath,
            PayloadWidth = header.PayloadWidth,
            PayloadHeight = header.PayloadHeight,
            ExtractedBytes = payloadBytes.Length
        };
    }

    /// <summary>
    /// Estimates the largest payload that can fit in a cover image after reserving space for header and compact metadata.
    /// </summary>
    /// <param name="coverWidth">Cover image width in pixels.</param>
    /// <param name="coverHeight">Cover image height in pixels.</param>
    /// <returns>An estimated maximum payload size in bytes.</returns>

    public int CalculateMaximumPayloadBytes(int coverWidth, int coverHeight)
    {
        var rgbChannelCount = checked(coverWidth * coverHeight * 3L);
        var headerBits = BinaryHeaderService.HeaderSizeBytes * 8L;
        if (rgbChannelCount <= headerBits)
        {
            return 0;
        }

        long low = 0;
        long high = Math.Max(0, (rgbChannelCount - headerBits) / 8);
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            var requiredBits = CalculateEstimatedTotalRequiredBits(rgbChannelCount, mid);
            if (requiredBits <= rgbChannelCount)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return (int)low;
    }

    /// <summary>
    /// Estimates the minimum number of pixels required to store a payload of the given size.
    /// </summary>
    /// <param name="payloadByteLength">Payload size in bytes.</param>
    /// <returns>An estimated minimum number of cover pixels.</returns>

    public long CalculateMinimumCoverPixels(long payloadByteLength)
    {
        if (payloadByteLength <= 0)
        {
            return 0;
        }

        long low = 1;
        long high = Math.Max(1, payloadByteLength * 16L);
        while (CalculateEstimatedTotalRequiredBits(high * 3L, payloadByteLength) > high * 3L)
        {
            high *= 2;
        }

        while (low < high)
        {
            var mid = (low + high) / 2;
            var rgbChannelCount = mid * 3L;
            if (CalculateEstimatedTotalRequiredBits(rgbChannelCount, payloadByteLength) <= rgbChannelCount)
            {
                high = mid;
            }
            else
            {
                low = mid + 1;
            }
        }

        return low;
    }

    /// <summary>
    /// Builds a rough estimate for UI display.
    /// The estimate combines expected metadata size, required bits, cover capacity, and a simple GA time model.
    /// </summary>
    /// <param name="coverWidth">Cover image width in pixels.</param>
    /// <param name="coverHeight">Cover image height in pixels.</param>
    /// <param name="payloadByteLength">Payload size in bytes.</param>
    /// <param name="parameters">GA settings that affect runtime and search effort.</param>
    /// <returns>An <see cref="OperationEstimate"/> for the UI.</returns>

    public OperationEstimate EstimateEmbedOperation(int coverWidth, int coverHeight, long payloadByteLength, GaParameters parameters)
    {
        var maximumPayloadBytes = CalculateMaximumPayloadBytes(coverWidth, coverHeight);
        var minimumCoverPixels = CalculateMinimumCoverPixels(payloadByteLength);
        var hasEnoughCapacity = payloadByteLength > 0 && payloadByteLength <= maximumPayloadBytes;

        var payloadBits = Math.Max(1L, payloadByteLength * 8L);
        var evaluations = parameters.PopulationSize * Math.Max(2, parameters.Generations + 1);
        var geneOperations = (double)payloadBits * evaluations;
        var effectiveCoreCount = Math.Max(1, Math.Min(2, Environment.ProcessorCount));
        var estimatedMinimumSeconds = Math.Max(2.0, geneOperations / (600_000d * effectiveCoreCount));
        var estimatedMaximumSeconds = Math.Max(estimatedMinimumSeconds * 2.5, geneOperations / (150_000d * effectiveCoreCount));

        var warning = string.Empty;
        if (payloadByteLength <= 0)
        {
            warning = "Select a payload image to calculate capacity and runtime estimates.";
        }
        else if (!hasEnoughCapacity)
        {
            warning = "Selected cover image does not have enough capacity for the payload under the current compact metadata format.";
        }
        else if (payloadByteLength > maximumPayloadBytes * 0.8)
        {
            warning = "Payload is close to the estimated cover capacity. Runtime may increase and image quality may decrease.";
        }

        return new OperationEstimate
        {
            MaximumPayloadBytes = maximumPayloadBytes,
            MinimumCoverPixels = minimumCoverPixels,
            EstimatedMinimumSeconds = estimatedMinimumSeconds,
            EstimatedMaximumSeconds = estimatedMaximumSeconds,
            HasEnoughCapacity = hasEnoughCapacity,
            WarningMessage = warning
        };
    }

    /// <summary>
    /// Estimates the total number of bits needed for header, metadata, and payload together.
    /// </summary>

    private static long CalculateEstimatedTotalRequiredBits(long rgbChannelCount, long payloadByteLength)
    {
        var payloadBits = checked(payloadByteLength * 8L);
        var metadataBytes = EstimateMetadataByteLength(rgbChannelCount, payloadBits);
        return checked((BinaryHeaderService.HeaderSizeBytes * 8L) + (metadataBytes * 8L) + payloadBits);
    }

    /// <summary>
    /// Estimates how many bytes are needed to store the compressed embedding map before the final exact metadata is built.
    /// </summary>

    private static int EstimateMetadataByteLength(long rgbChannelCount, long payloadBitCount)
    {
        if (payloadBitCount <= 0)
        {
            return 1;
        }

        var usableChannels = Math.Max(1L, rgbChannelCount - (BinaryHeaderService.HeaderSizeBytes * 8L));
        var averageGap = Math.Max(0.0, (usableChannels / (double)payloadBitCount) - 1.0);
        var riceParameter = ChooseRiceParameter(averageGap);
        var averageBitsPerGene = 1.0 + riceParameter + (averageGap / (1 << riceParameter));
        var totalBits = 8.0 + (payloadBitCount * averageBitsPerGene);
        return Math.Max(1, (int)Math.Ceiling((totalBits / 8.0) * MetadataSafetyFactor));
    }

    /// <summary>
    /// Shifts all payload positions forward when the reserved metadata area grows.
    /// This keeps header and metadata at the beginning of the image without overlapping payload bits.
    /// </summary>

    private static int[] ShiftGenes(IReadOnlyList<int> genes, int shiftBits, int rgbChannelCount)
    {
        if (shiftBits <= 0)
        {
            return genes.ToArray();
        }

        var shifted = new int[genes.Count];
        for (var i = 0; i < genes.Count; i++)
        {
            shifted[i] = checked(genes[i] + shiftBits);
            if (shifted[i] >= rgbChannelCount)
            {
                throw new InvalidOperationException("Cover image does not have enough trailing capacity after accounting for embedding metadata.");
            }
        }

        return shifted;
    }

    /// <summary>
    /// Creates and sends a progress report object to the UI.
    /// </summary>
    /// <param name="progress">Optional receiver of progress updates.</param>
    /// <param name="phase">Current high-level phase name.</param>
    /// <param name="completedUnits">Completed work units inside the phase.</param>
    /// <param name="totalUnits">Total work units inside the phase.</param>
    /// <param name="percent">Overall percent for the progress bar.</param>
    /// <param name="elapsed">Elapsed time since the operation started.</param>
    /// <param name="remaining">Estimated remaining time.</param>
    /// <param name="message">Short user-facing progress message.</param>

    private static void Report(
        IProgress<OperationProgress>? progress,
        string phase,
        int completedUnits,
        int totalUnits,
        double percent,
        TimeSpan elapsed,
        TimeSpan remaining,
        string message)
    {
        progress?.Report(new OperationProgress
        {
            Phase = phase,
            CompletedUnits = completedUnits,
            TotalUnits = totalUnits,
            Percent = percent,
            Elapsed = elapsed,
            EstimatedRemaining = remaining,
            Message = message
        });
    }

    /// <summary>
    /// Compresses the sorted embedding positions into metadata bytes using delta coding plus Rice coding.
    /// </summary>

    private static byte[] SerializeGenesCompressed(IReadOnlyList<int> genes, int prefixBitsCount)
    {
        var averageDelta = CalculateAverageDelta(genes, prefixBitsCount);
        var riceParameter = ChooseRiceParameter(averageDelta);

        using var output = new MemoryStream();
        output.WriteByte((byte)riceParameter);

        var writer = new PackedBitWriter(output);
        var previous = 0;
        for (var i = 0; i < genes.Count; i++)
        {
            var delta = i == 0 ? genes[i] - prefixBitsCount : genes[i] - previous - 1;
            writer.WriteRice(delta, riceParameter);
            previous = genes[i];
        }

        writer.Flush();
        return output.ToArray();
    }

    /// <summary>
    /// Rebuilds the embedding position list from the compressed metadata representation.
    /// </summary>

    private static int[] DeserializeGenesCompressed(byte[] metadataBytes, int expectedGeneCount, int prefixBitsCount)
    {
        if (metadataBytes.Length == 0)
        {
            throw new InvalidOperationException("Embedding map metadata is empty.");
        }

        var riceParameter = metadataBytes[0];
        if (riceParameter > 15)
        {
            throw new InvalidOperationException("Embedding map uses an invalid Rice parameter.");
        }

        using var input = new MemoryStream(metadataBytes, 1, metadataBytes.Length - 1, writable: false);
        var reader = new PackedBitReader(input);
        var genes = new int[expectedGeneCount];
        var previous = 0;
        for (var i = 0; i < expectedGeneCount; i++)
        {
            var delta = reader.ReadRice(riceParameter);
            genes[i] = i == 0 ? checked(prefixBitsCount + delta) : checked(previous + 1 + delta);
            previous = genes[i];
        }

        return genes;
    }

    /// <summary>
    /// Selects the Rice coding parameter based on the average gap between embedding positions.
    /// Rice coding is an entropy encoding method that uses a parameter (0-7) to efficiently represent gaps in data.
    /// </summary>

    private static int ChooseRiceParameter(double averageGap)
    {
        if (averageGap <= 0)
        {
            return 0;
        }

        var parameter = (int)Math.Round(Math.Log(averageGap + 1.0, 2));
        return Math.Clamp(parameter, 0, 7);
    }

    /// <summary>
    /// Pads metadata to the reserved byte length so the prefix layout stays fixed.
    /// </summary>


    private static byte[] PadMetadata(byte[] metadataBytes, int reservedMetadataByteLength)
    {
        if (metadataBytes.Length > reservedMetadataByteLength)
        {
            throw new InvalidOperationException("Metadata is larger than the reserved metadata region.");
        }

        if (metadataBytes.Length == reservedMetadataByteLength)
        {
            return metadataBytes;
        }

        var padded = new byte[reservedMetadataByteLength];
        Buffer.BlockCopy(metadataBytes, 0, padded, 0, metadataBytes.Length);
        return padded;
    }

    /// <summary>
    /// Computes the average gap between consecutive embedding positions.
    /// This helps choose a more suitable Rice parameter.
    /// </summary>

    private static double CalculateAverageDelta(IReadOnlyList<int> genes, int prefixBitsCount)
    {
        if (genes.Count == 0)
        {
            return 0.0;
        }

        long total = 0;
        var previous = prefixBitsCount - 1;
        for (var i = 0; i < genes.Count; i++)
        {
            var delta = genes[i] - previous - 1;
            total += Math.Max(0, delta);
            previous = genes[i];
        }

        return total / (double)genes.Count;
    }

    /// <summary>
    /// Helper that writes packed bits into a byte stream while building compressed metadata.
    /// </summary>
    /// <remarks>This helper exists only inside the coordinator because it is part of the metadata packing implementation.</remarks>

    private class PackedBitWriter
    {
        private readonly Stream stream;
        private int bitCount;
        private int currentByte;

        /// <summary>
        /// Creates a packed bit writer over the provided stream.
        /// </summary>

        public PackedBitWriter(Stream stream)
        {
            this.stream = stream;
        }

        /// <summary>
        /// Writes one non-negative integer using Rice coding.
        /// </summary>

        public void WriteRice(int value, int parameter)
        {
            if (value < 0)
            {
                throw new InvalidOperationException("Rice coding received a negative value.");
            }

            var quotient = value >> parameter;
            for (var i = 0; i < quotient; i++)
            {
                WriteBit(0);
            }

            WriteBit(1);

            for (var bit = parameter - 1; bit >= 0; bit--)
            {
                WriteBit((value >> bit) & 1);
            }
        }

        /// <summary>
        /// Flushes any remaining buffered bits into the final byte.
        /// </summary>

        public void Flush()
        {
            if (this.bitCount == 0)
            {
                return;
            }

            this.currentByte <<= (8 - this.bitCount);
            this.stream.WriteByte((byte)this.currentByte);
            this.bitCount = 0;
            this.currentByte = 0;
        }

        /// <summary>
        /// Writes a single bit into the internal byte buffer.
        /// </summary>

        private void WriteBit(int bit)
        {
            this.currentByte = (this.currentByte << 1) | (bit & 1);
            this.bitCount++;
            if (this.bitCount == 8)
            {
                this.stream.WriteByte((byte)this.currentByte);
                this.bitCount = 0;
                this.currentByte = 0;
            }
        }
    }

    /// <summary>
    /// Helper that reads packed bits from a byte stream while decoding compressed metadata.
    /// </summary>
    /// <remarks>This helper exists only inside the coordinator because it is part of the metadata unpacking implementation.</remarks>

    private class PackedBitReader
    {
        private readonly Stream stream;
        private int bitsRemaining;
        private int currentByte;

        /// <summary>
        /// Creates a packed bit reader over the provided stream.
        /// </summary>

        public PackedBitReader(Stream stream)
        {
            this.stream = stream;
        }

        /// <summary>
        /// Reads one integer encoded with Rice coding.
        /// </summary>

        public int ReadRice(int parameter)
        {
            var quotient = 0;
            while (ReadBit() == 0)
            {
                quotient++;
            }

            var remainder = 0;
            for (var i = 0; i < parameter; i++)
            {
                remainder = (remainder << 1) | ReadBit();
            }

            return (quotient << parameter) | remainder;
        }

        /// <summary>
        /// Reads a single bit from the current byte buffer, loading a new byte from the stream when needed.
        /// </summary>

        private int ReadBit()
        {
            if (this.bitsRemaining == 0)
            {
                this.currentByte = this.stream.ReadByte();
                if (this.currentByte < 0)
                {
                    throw new InvalidOperationException("Unexpected end of metadata while reading the embedding map.");
                }

                this.bitsRemaining = 8;
            }

            var bit = (this.currentByte >> (this.bitsRemaining - 1)) & 1;
            this.bitsRemaining--;
            return bit;
        }
    }

    /// <summary>
    /// Validates that the extracted header looks consistent and still fits inside the available cover capacity.
    /// </summary>

    private static void ValidateHeader(HeaderInfo header, int rgbChannelCount)
    {
        if (header.PayloadWidth <= 0 || header.PayloadHeight <= 0 || header.PayloadByteLength <= 0 || header.MetadataByteLength <= 0)
        {
            throw new InvalidOperationException("Header contains invalid values. The key may be incorrect or the stego data is corrupted.");
        }

        var prefixBits = checked((BinaryHeaderService.HeaderSizeBytes + header.MetadataByteLength) * 8);
        var payloadBits = checked(header.PayloadByteLength * 8);
        EnsureCapacity(rgbChannelCount, prefixBits, payloadBits);
    }

    /// <summary>
    /// Validates that extracted embedding positions are sorted, unique, and inside the legal payload area.
    /// </summary>

    private static void ValidateGenes(int[] genes, int prefixBitsCount, int rgbChannelCount)
    {
        if (genes.Length == 0)
        {
            throw new InvalidOperationException("Embedding map is empty.");
        }

        var previous = -1;
        for (var i = 0; i < genes.Length; i++)
        {
            var gene = genes[i];
            if (gene < prefixBitsCount || gene >= rgbChannelCount)
            {
                throw new InvalidOperationException("Metadata contains an embedding position outside the allowed payload area.");
            }

            if (gene <= previous)
            {
                throw new InvalidOperationException("Metadata contains duplicate or unsorted embedding positions.");
            }

            previous = gene;
        }
    }

    /// <summary>
    /// Checks that recovered payload bytes start with the standard PNG file signature.
    /// </summary>

    private static void ValidatePngSignature(byte[] payloadBytes)
    {
        if (payloadBytes.Length < PngSignature.Length)
        {
            throw new InvalidOperationException("Recovered payload is too short to be a valid PNG file.");
        }

        for (var i = 0; i < PngSignature.Length; i++)
        {
            if (payloadBytes[i] != PngSignature[i])
            {
                throw new InvalidOperationException("Recovered payload does not contain a valid PNG signature.");
            }
        }
    }

    /// <summary>
    /// Ensures that the cover image has enough usable RGB channels for header, metadata, and payload.
    /// </summary>

    private static void EnsureCapacity(int rgbChannelCount, int prefixBits, int payloadBits)
    {
        if (rgbChannelCount <= 0)
        {
            throw new InvalidOperationException("Cover image must contain enough RGB channel capacity for embedding.");
        }

        var usableBits = rgbChannelCount - prefixBits;
        if (usableBits <= 0)
        {
            throw new InvalidOperationException("Cover image does not have any payload capacity left after reserving the header and metadata area.");
        }

        if (payloadBits > usableBits)
        {
            throw new InvalidOperationException("Cover image capacity is insufficient for the requested payload under the current compact metadata format.");
        }
    }

    /// <summary>
    /// Writes a contiguous run of bits into consecutive logical RGB channels.
    /// Used for the encrypted header and encrypted metadata area.
    /// </summary>

    private void WriteSequentialBits(byte[] pixels, int logicalChannelStart, IReadOnlyList<int> bits)
    {
        for (var i = 0; i < bits.Count; i++)
        {
            this.lsbService.WriteBit(pixels, logicalChannelStart + i, bits[i]);
        }
    }

    /// <summary>
    /// Reads a contiguous run of bits from consecutive logical RGB channels.
    /// </summary>

    private int[] ReadSequentialBits(byte[] pixels, int logicalChannelStart, int bitCount)
    {
        var bits = new int[bitCount];
        for (var i = 0; i < bitCount; i++)
        {
            bits[i] = this.lsbService.ReadBit(pixels, logicalChannelStart + i);
        }

        return bits;
    }

    /// <summary>
    /// Counts how many RGB channels changed between the original cover image and the final stego image.
    /// </summary>

    private static int CountChangedChannels(byte[] originalPixels, byte[] modifiedPixels)
    {
        var changes = 0;
        for (var i = 0; i < originalPixels.Length; i += 4)
        {
            if (originalPixels[i + 0] != modifiedPixels[i + 0]) changes++;
            if (originalPixels[i + 1] != modifiedPixels[i + 1]) changes++;
            if (originalPixels[i + 2] != modifiedPixels[i + 2]) changes++;
        }

        return changes;
    }
}
