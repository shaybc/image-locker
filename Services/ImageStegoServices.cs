namespace ImageLocker.Services;

/// <summary>
/// Performs single-bit LSB reads and writes on logical RGB channels.
/// This is the low-level service that actually hides or extracts one bit at a time.
/// </summary>
public class LsbSteganographyService
{

    /// <summary>
    /// Writes one payload bit into one logical RGB channel by changing only the least significant bit.
    /// Returns true when the pixel buffer had to be modified.
    /// </summary>
    /// <param name="pixels">BGRA32 pixel buffer to modify.</param>
    /// <param name="logicalRgbChannelIndex">Logical RGB channel index that ignores alpha bytes.</param>
    /// <param name="bit">Bit value to write. Must be 0 or 1.</param>
    /// <returns><c>true</c> if one byte changed; otherwise <c>false</c>.</returns>
    public bool WriteBit(byte[] pixels, int logicalRgbChannelIndex, int bit)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        if (bit is not (0 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(bit), "Bit must be 0 or 1.");
        }

        var offset = ChannelIndexHelper.ToByteOffset(logicalRgbChannelIndex);
        if (offset < 0 || offset >= pixels.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(logicalRgbChannelIndex), "Channel index is outside the pixel buffer.");
        }

        var value = pixels[offset];
        var currentBit = value & 1;
        if (currentBit == bit)
        {
            return false;
        }

        if (value == 0)
        {
            pixels[offset] = 1;
        }
        else if (value == 255)
        {
            pixels[offset] = 254;
        }
        else if (currentBit == 0)
        {
            pixels[offset] = (byte)(value + 1);
        }
        else
        {
            pixels[offset] = (byte)(value - 1);
        }

        return true;
    }

    /// <summary>
    /// Reads the least significant bit from one logical RGB channel.
    /// </summary>
    /// <param name="pixels">BGRA32 pixel buffer to read from.</param>
    /// <param name="logicalRgbChannelIndex">Logical RGB channel index that ignores alpha bytes.</param>
    /// <returns>The extracted bit value, 0 or 1.</returns>

    public int ReadBit(byte[] pixels, int logicalRgbChannelIndex)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        var offset = ChannelIndexHelper.ToByteOffset(logicalRgbChannelIndex);
        if (offset < 0 || offset >= pixels.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(logicalRgbChannelIndex), "Channel index is outside the pixel buffer.");
        }

        return pixels[offset] & 1;
    }
}

/// <summary>
/// Computes image quality metrics used to judge how visible the embedding changes are.
/// </summary>
public class MetricsService
{

    /// <summary>
    /// Computes mean squared error on RGB channels only.
    /// Alpha is ignored because the project embeds data only in visible color channels.
    /// </summary>
    /// <param name="originalPixels">Original cover image pixels.</param>
    /// <param name="modifiedPixels">Modified stego image pixels.</param>
    /// <returns>The RGB-only MSE value.</returns>
    public double ComputeMseRgb(byte[] originalPixels, byte[] modifiedPixels)
    {
        ArgumentNullException.ThrowIfNull(originalPixels);
        ArgumentNullException.ThrowIfNull(modifiedPixels);

        if (originalPixels.Length != modifiedPixels.Length)
        {
            throw new InvalidOperationException("Pixel arrays must have equal length.");
        }

        if (originalPixels.Length % 4 != 0)
        {
            throw new InvalidOperationException("Pixel array length must be divisible by 4 for BGRA32.");
        }

        double sum = 0;
        var comparedChannels = 0;

        for (var i = 0; i < originalPixels.Length; i += 4)
        {
            var diffB = originalPixels[i + 0] - modifiedPixels[i + 0];
            var diffG = originalPixels[i + 1] - modifiedPixels[i + 1];
            var diffR = originalPixels[i + 2] - modifiedPixels[i + 2];

            sum += (diffB * diffB) + (diffG * diffG) + (diffR * diffR);
            comparedChannels += 3;
        }

        return comparedChannels == 0 ? 0 : sum / comparedChannels;
    }

    /// <summary>
    /// Computes PSNR directly from two images by first calculating MSE.
    /// </summary>
    /// <param name="originalPixels">Original cover image pixels.</param>
    /// <param name="modifiedPixels">Modified stego image pixels.</param>
    /// <returns>The PSNR value in decibels.</returns>

    public double ComputePsnrRgb(byte[] originalPixels, byte[] modifiedPixels)
    {
        var mse = ComputeMseRgb(originalPixels, modifiedPixels);
        return ComputePsnrFromMse(mse);
    }

    /// <summary>
    /// Converts an MSE value into PSNR using the standard 8-bit image formula.
    /// </summary>
    /// <param name="mse">Mean squared error value.</param>
    /// <returns>The PSNR value in decibels, or positive infinity when MSE is zero.</returns>

    public double ComputePsnrFromMse(double mse)
    {
        if (mse <= 0)
        {
            return double.PositiveInfinity;
        }

        // 255 is the maximum value of one 8-bit color channel in standard PNG pixel data.
        const double maxIntensity = 255.0;
        return 10.0 * Math.Log10((maxIntensity * maxIntensity) / mse);
    }
}
