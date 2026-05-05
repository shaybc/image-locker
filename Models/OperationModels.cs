namespace ImageLocker.Models;

/// <summary>
/// Result object returned by the embed operation.
/// Contains output path plus quality and size statistics.
/// </summary>
public class EmbedResult
{
    public string OutputPath { get; init; } = string.Empty;
    public int HeaderBits { get; init; }
    public int MetadataBits { get; init; }
    public int PayloadBits { get; init; }
    public int ChangedChannels { get; init; }
    public double Mse { get; init; }
    public double Psnr { get; init; }
}

/// <summary>
/// Result object returned by the extract operation.
/// Contains the saved file path and recovered payload information.
/// </summary>
public class ExtractionResult
{
    public string OutputPath { get; init; } = string.Empty;
    public int PayloadWidth { get; init; }
    public int PayloadHeight { get; init; }
    public int ExtractedBytes { get; init; }
}

/// <summary>
/// Small DTO (Data Transfer Object) that stores rough time and capacity estimates shown before running the algorithm.
/// </summary>
public class OperationEstimate
{
    public int MaximumPayloadBytes { get; init; }
    public long MinimumCoverPixels { get; init; }
    public double EstimatedMinimumSeconds { get; init; }
    public double EstimatedMaximumSeconds { get; init; }
    public bool HasEnoughCapacity { get; init; }
    public string WarningMessage { get; init; } = string.Empty;
}

/// <summary>
/// Progress report object sent from the service layer to the UI during long operations.
/// </summary>
public class OperationProgress
{
    public string Phase { get; init; } = string.Empty;
    public int CompletedUnits { get; init; }
    public int TotalUnits { get; init; }
    public double Percent { get; init; }
    public TimeSpan Elapsed { get; init; }
    public TimeSpan EstimatedRemaining { get; init; }
    public string Message { get; init; } = string.Empty;
}
