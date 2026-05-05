using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;
using ImageLocker.Models;
using ImageLocker.Services;

namespace ImageLocker.ViewModels;

/// <summary>
/// Coordinates the main screen.
/// It stores UI state, validates input, starts embed/extract operations, and updates previews, progress, and status text.
/// </summary>
public class MainViewModel : BaseViewModel
{
    private readonly SteganographyCoordinator steganographyCoordinator;
    private readonly ImageIOService imageIoService;

    private string payloadPath = string.Empty;
    private string coverPath = string.Empty;
    private string stegoInputPath = string.Empty;
    private string outputFolderPath = string.Empty;
    private string outputFileName = "StegoOutput";
    private string key = string.Empty;
    private int populationSize = 8;
    private int generations = 4;
    private double mutationRate = 0.10;
    private string status = "Ready.";
    private bool isBusy;
    private bool isEmbedMode = true;
    private double progressValue;
    private string progressText = "Idle.";
    private string progressEtaText = string.Empty;
    private string operationEstimateText = "Load images to see estimated time and capacity.";
    private string capacityWarningText = string.Empty;

    private ImageSource? payloadPreview;
    private ImageSource? coverPreview;
    private ImageSource? resultPreview;
    private ImageSource? stegoPreview;

    private string payloadInfoText = "Select the image you want to hide.";
    private string payloadCapacityText = string.Empty;
    private string coverInfoText = "Select the cover image.";
    private string coverCapacityText = string.Empty;
    private string stegoInfoText = "Select the stego image for extraction.";
    private string extractInfoText = "Select the stego image and the XOR key, then extract.";

    /// <summary>
    /// Creates the main view model and wires all commands.
    /// Also sets default values and performs the first UI refresh.
    /// </summary>
    /// <param name="steganographyCoordinator">Main service that runs embed and extract operations.</param>
    /// <param name="imageIoService">Image loading service used for previews and image info.</param>

    public MainViewModel(SteganographyCoordinator steganographyCoordinator, ImageIOService imageIoService)
    {
        this.steganographyCoordinator = steganographyCoordinator;
        this.imageIoService = imageIoService;
        this.outputFolderPath = GetDefaultOutputFolderPath();

        this.SetEmbedModeCommand = new RelayCommand(this.SetEmbedMode, () => !this.IsBusy);
        this.SetExtractModeCommand = new RelayCommand(this.SetExtractMode, () => !this.IsBusy);

        this.BrowsePayloadCommand = new RelayCommand(this.BrowsePayload, () => !this.IsBusy);
        this.BrowseCoverCommand = new RelayCommand(this.BrowseCover, () => !this.IsBusy);
        this.BrowseStegoInputCommand = new RelayCommand(this.BrowseStegoInput, () => !this.IsBusy);
        this.BrowseOutputFolderCommand = new RelayCommand(this.BrowseOutputFolder, () => !this.IsBusy);

        this.EmbedCommand = new RelayCommand(() => _ = this.EmbedAsync(), () => !this.IsBusy);
        this.ExtractCommand = new RelayCommand(() => _ = this.ExtractAsync(), () => !this.IsBusy);

        this.RefreshPathDependentState();
    }

    public string PayloadPath
    {
        get => this.payloadPath;
        set
        {
            var normalizedPath = NormalizePngInputPath(value);
            if (this.SetProperty(ref this.payloadPath, normalizedPath))
            {
                this.ResultPreview = null;
                this.RefreshPathDependentState();
            }
        }
    }

    public string CoverPath
    {
        get => this.coverPath;
        set
        {
            var normalizedPath = NormalizePngInputPath(value);
            if (this.SetProperty(ref this.coverPath, normalizedPath))
            {
                this.ResultPreview = null;
                this.RefreshPathDependentState();
            }
        }
    }

    public string StegoInputPath
    {
        get => this.stegoInputPath;
        set
        {
            var normalizedPath = NormalizePngInputPath(value);
            if (this.SetProperty(ref this.stegoInputPath, normalizedPath))
            {
                this.ResultPreview = null;
                this.RefreshPathDependentState();
            }
        }
    }

    public string OutputFolderPath
    {
        get => this.outputFolderPath;
        set => this.SetProperty(ref this.outputFolderPath, value);
    }

    public string OutputFileName
    {
        get => this.outputFileName;
        set => this.SetProperty(ref this.outputFileName, value);
    }

    public string Key
    {
        get => this.key;
        set => this.SetProperty(ref this.key, value);
    }

    public int PopulationSize
    {
        get => this.populationSize;
        set
        {
            if (this.SetProperty(ref this.populationSize, value))
            {
                this.RefreshComputedTexts();
            }
        }
    }

    public int Generations
    {
        get => this.generations;
        set
        {
            if (this.SetProperty(ref this.generations, value))
            {
                this.RefreshComputedTexts();
            }
        }
    }

    public double MutationRate
    {
        get => this.mutationRate;
        set
        {
            if (this.SetProperty(ref this.mutationRate, value))
            {
                this.RefreshComputedTexts();
            }
        }
    }

    public string Status
    {
        get => this.status;
        set => this.SetProperty(ref this.status, value);
    }

    public bool IsBusy
    {
        get => this.isBusy;
        private set
        {
            if (this.SetProperty(ref this.isBusy, value))
            {
                this.RefreshCommands();
            }
        }
    }

    public bool IsEmbedMode
    {
        get => this.isEmbedMode;
        private set
        {
            if (this.SetProperty(ref this.isEmbedMode, value))
            {
                this.RaisePropertyChanged(nameof(IsExtractMode));
                this.RefreshCommands();
            }
        }
    }

    public bool IsExtractMode => !IsEmbedMode;

    public double ProgressValue
    {
        get => this.progressValue;
        private set => this.SetProperty(ref this.progressValue, value);
    }

    public string ProgressText
    {
        get => this.progressText;
        private set => this.SetProperty(ref this.progressText, value);
    }

    public string ProgressEtaText
    {
        get => this.progressEtaText;
        private set => this.SetProperty(ref this.progressEtaText, value);
    }

    public string OperationEstimateText
    {
        get => this.operationEstimateText;
        private set => this.SetProperty(ref this.operationEstimateText, value);
    }

    public string CapacityWarningText
    {
        get => this.capacityWarningText;
        private set => this.SetProperty(ref this.capacityWarningText, value);
    }

    public ImageSource? PayloadPreview
    {
        get => this.payloadPreview;
        private set => this.SetProperty(ref this.payloadPreview, value);
    }

    public ImageSource? CoverPreview
    {
        get => this.coverPreview;
        private set => this.SetProperty(ref this.coverPreview, value);
    }

    public ImageSource? ResultPreview
    {
        get => this.resultPreview;
        private set => this.SetProperty(ref this.resultPreview, value);
    }

    public ImageSource? StegoPreview
    {
        get => this.stegoPreview;
        private set => this.SetProperty(ref this.stegoPreview, value);
    }

    public string PayloadInfoText
    {
        get => this.payloadInfoText;
        private set => this.SetProperty(ref this.payloadInfoText, value);
    }

    public string PayloadCapacityText
    {
        get => this.payloadCapacityText;
        private set => this.SetProperty(ref this.payloadCapacityText, value);
    }

    public string CoverInfoText
    {
        get => this.coverInfoText;
        private set => this.SetProperty(ref this.coverInfoText, value);
    }

    public string CoverCapacityText
    {
        get => this.coverCapacityText;
        private set => this.SetProperty(ref this.coverCapacityText, value);
    }

    public string StegoInfoText
    {
        get => this.stegoInfoText;
        private set => this.SetProperty(ref this.stegoInfoText, value);
    }

    public string ExtractInfoText
    {
        get => this.extractInfoText;
        private set => this.SetProperty(ref this.extractInfoText, value);
    }

    public RelayCommand SetEmbedModeCommand { get; }
    public RelayCommand SetExtractModeCommand { get; }
    public RelayCommand BrowsePayloadCommand { get; }
    public RelayCommand BrowseCoverCommand { get; }
    public RelayCommand BrowseStegoInputCommand { get; }
    public RelayCommand BrowseOutputFolderCommand { get; }
    public RelayCommand EmbedCommand { get; }
    public RelayCommand ExtractCommand { get; }

    /// <summary>
    /// Runs the full embed flow from the UI side.
    /// It validates the current form values, calls the coordinator, and then updates progress, previews, and result text.
    /// </summary>
    /// <returns>A task that completes when the embed workflow finishes.</returns>

    private async Task EmbedAsync()
    {
        try
        {
            this.IsBusy = true;
            this.ResultPreview = null;
            this.ProgressValue = 0;
            this.ProgressText = "Preparing embed operation.";
            this.ProgressEtaText = string.Empty;
            this.Status = "Embedding...";

            var parameters = this.BuildValidatedParameters();
            var outputPath = this.BuildOutputPath();
            var progress = new Progress<OperationProgress>(OnOperationProgress);

            var result = await Task.Run(() =>
                this.steganographyCoordinator.Embed(
                    coverPath: this.CoverPath,
                    payloadPath: this.PayloadPath,
                    stegoOutputPath: outputPath,
                    key: this.Key,
                    parameters: parameters,
                    progress: progress));

            this.ResultPreview = LoadPreview(result.OutputPath);
            this.ProgressValue = 100;
            this.ProgressText = "Embed completed.";

            this.Status = new StringBuilder()
                .AppendLine("Embed finished successfully.")
                .AppendLine($"Stego output: {result.OutputPath}")
                .AppendLine($"Header bits: {result.HeaderBits}")
                .AppendLine($"Metadata bits: {result.MetadataBits}")
                .AppendLine($"Payload bits: {result.PayloadBits}")
                .AppendLine($"Changed channels: {result.ChangedChannels}")
                .AppendLine($"MSE: {result.Mse:F6}")
                .AppendLine($"PSNR: {result.Psnr:F6} dB")
                .ToString();
        }
        catch (Exception ex)
        {
            this.ResultPreview = null;
            this.ProgressValue = 0;
            this.ProgressText = "Embed failed.";
            this.ProgressEtaText = string.Empty;
            this.Status = $"Embed failed: {ex.Message}";
        }
        finally
        {
            this.IsBusy = false;
            this.RefreshComputedTexts();
        }
    }

    /// <summary>
    /// Runs the full extract flow from the UI side.
    /// It reads the current stego path and key, calls the coordinator, and updates the screen with the extraction result.
    /// </summary>
    /// <returns>A task that completes when the extract workflow finishes.</returns>

    private async Task ExtractAsync()
    {
        try
        {
            this.IsBusy = true;
            this.ResultPreview = null;
            this.ProgressValue = 0;
            this.ProgressText = "Preparing extraction.";
            this.ProgressEtaText = string.Empty;
            this.Status = "Extracting...";

            var outputPath = this.BuildOutputPath();
            var progress = new Progress<OperationProgress>(OnOperationProgress);

            var result = await Task.Run(() =>
                this.steganographyCoordinator.Extract(
                    stegoPath: this.StegoInputPath,
                    payloadOutputPath: outputPath,
                    key: this.Key,
                    progress: progress));

            this.ResultPreview = LoadPreview(result.OutputPath);
            this.ProgressValue = 100;
            this.ProgressText = "Extraction completed.";
            this.ExtractInfoText = $"Extracted image: {result.PayloadWidth}×{result.PayloadHeight} | PNG file: {FormatBytes(result.ExtractedBytes)}";

            this.Status = new StringBuilder()
                .AppendLine("Extraction finished successfully.")
                .AppendLine($"Output: {result.OutputPath}")
                .AppendLine($"Payload width: {result.PayloadWidth}")
                .AppendLine($"Payload height: {result.PayloadHeight}")
                .AppendLine($"Extracted bytes: {result.ExtractedBytes}")
                .ToString();
        }
        catch (Exception ex)
        {
            this.ResultPreview = null;
            this.ProgressValue = 0;
            this.ProgressText = "Extraction failed.";
            this.ProgressEtaText = string.Empty;
            this.Status = $"Extraction failed: {ex.Message}";
        }
        finally
        {
            this.IsBusy = false;
            this.RefreshComputedTexts();
        }
    }

    /// <summary>
    /// Receives progress reports from the service layer.
    /// Converts raw progress data into the text and percentage shown in the UI.
    /// </summary>
    /// <param name="progress">Progress snapshot reported by the service layer.</param>

    private void OnOperationProgress(OperationProgress progress)
    {
        this.ProgressValue = progress.Percent;
        this.ProgressText = $"{progress.Phase}: {progress.Message}";
        this.ProgressEtaText = $"Elapsed: {FormatDuration(progress.Elapsed)} | Remaining: {FormatDuration(progress.EstimatedRemaining)}";
    }

    /// <summary>
    /// Switches the screen into embed mode.
    /// Used when the same window supports both embedding and extraction.
    /// </summary>

    private void SetEmbedMode()
    {
        this.IsEmbedMode = true;
        this.ResultPreview = null;
        if (string.IsNullOrWhiteSpace(this.OutputFileName) || this.OutputFileName == "ExtractedPayload")
        {
            this.OutputFileName = "StegoOutput";
        }
    }

    /// <summary>
    /// Switches the screen into extract mode.
    /// It also refreshes command availability and visible text.
    /// </summary>

    private void SetExtractMode()
    {
        this.IsEmbedMode = false;
        this.ResultPreview = null;
        this.ExtractInfoText = "Select the stego image and the XOR key, then extract.";
        if (string.IsNullOrWhiteSpace(this.OutputFileName) || this.OutputFileName == "StegoOutput")
        {
            this.OutputFileName = "ExtractedPayload";
        }
    }

    /// <summary>
    /// Builds a validated GA parameter object from the current UI values.
    /// Throws if the user entered an invalid population size, generation count, or mutation rate.
    /// </summary>
    /// <returns>A validated <see cref="GaParameters"/> instance.</returns>

    private GaParameters BuildValidatedParameters()
    {
        var parameters = new GaParameters
        {
            PopulationSize = this.PopulationSize,
            Generations = this.Generations,
            MutationRate = this.MutationRate
        };

        parameters.Validate();
        return parameters;
    }

    /// <summary>
    /// Opens a file dialog for the hidden image path and stores the selected PNG path.
    /// </summary>

    private void BrowsePayload()
    {
        var dialog = CreateOpenPngDialog();
        if (dialog.ShowDialog() == true)
        {
            this.PayloadPath = dialog.FileName;
        }
    }

    /// <summary>
    /// Opens a file dialog for the cover image path and stores the selected PNG path.
    /// </summary>

    private void BrowseCover()
    {
        var dialog = CreateOpenPngDialog();
        if (dialog.ShowDialog() == true)
        {
            this.CoverPath = dialog.FileName;
        }
    }

    /// <summary>
    /// Opens a file dialog for the stego image that will be used during extraction.
    /// </summary>

    private void BrowseStegoInput()
    {
        var dialog = CreateOpenPngDialog();
        if (dialog.ShowDialog() == true)
        {
            this.StegoInputPath = dialog.FileName;
        }
    }

    /// <summary>
    /// Opens a folder picker for the output directory used by embed and extract operations.
    /// </summary>

    private void BrowseOutputFolder()
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Select output folder"
        };

        if (Directory.Exists(this.OutputFolderPath))
        {
            dialog.SelectedPath = this.OutputFolderPath;
        }

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            this.OutputFolderPath = dialog.SelectedPath;
        }
    }

    /// <summary>
    /// Refreshes all UI elements that depend on the currently selected file paths.
    /// This includes previews, info labels, capacity text, and estimates.
    /// </summary>

    private void RefreshPathDependentState()
    {
        this.PayloadPreview = LoadPreview(this.PayloadPath);
        this.CoverPreview = LoadPreview(this.CoverPath);
        this.StegoPreview = LoadPreview(this.StegoInputPath);
        this.RefreshComputedTexts();
    }

    /// <summary>
    /// Recomputes helper text such as estimated operation time, payload capacity, and warnings.
    /// Used whenever paths or GA settings change.
    /// </summary>

    private void RefreshComputedTexts()
    {
        if (this.TryGetImageData(this.PayloadPath, out var payloadImage, out var payloadFileSize) && payloadImage is not null)
        {
            this.PayloadInfoText = $"Payload: {payloadImage.Width}×{payloadImage.Height} | PNG file: {FormatBytes(payloadFileSize)}";
        }
        else
        {
            this.PayloadInfoText = "Select the image you want to hide.";
        }

        if (this.TryGetImageData(this.CoverPath, out var coverImage, out var coverFileSize) && coverImage is not null)
        {
            this.CoverInfoText = $"Cover: {coverImage.Width}×{coverImage.Height} | PNG file: {FormatBytes(coverFileSize)}";
            var maxPayload = this.steganographyCoordinator.CalculateMaximumPayloadBytes(coverImage.Width, coverImage.Height);
            this.CoverCapacityText = $"Estimated maximum payload with compact embedded metadata: {FormatBytes(maxPayload)}";
        }
        else
        {
            this.CoverInfoText = "Select the cover image.";
            this.CoverCapacityText = string.Empty;
        }

        if (this.TryGetImageData(this.PayloadPath, out _, out payloadFileSize))
        {
            var minPixels = this.steganographyCoordinator.CalculateMinimumCoverPixels(payloadFileSize);
            var minSide = minPixels <= 0 ? 0 : (int)Math.Ceiling(Math.Sqrt(minPixels));
            this.PayloadCapacityText = minPixels <= 0
                ? string.Empty
                : $"Minimum cover size for this payload: {minPixels:N0} pixels (~{minSide}×{minSide}).";
        }
        else
        {
            this.PayloadCapacityText = string.Empty;
        }

        if (this.TryGetImageData(this.CoverPath, out coverImage, out _) && coverImage is not null &&
            this.TryGetImageData(this.PayloadPath, out _, out payloadFileSize))
        {
            var estimate = this.steganographyCoordinator.EstimateEmbedOperation(coverImage.Width, coverImage.Height, payloadFileSize, this.BuildValidatedParametersSafe());
            this.OperationEstimateText = $"Estimated embed time: about {FormatDuration(TimeSpan.FromSeconds(estimate.EstimatedMinimumSeconds))} to {FormatDuration(TimeSpan.FromSeconds(estimate.EstimatedMaximumSeconds))}";
            this.CapacityWarningText = estimate.WarningMessage;
        }
        else
        {
            this.OperationEstimateText = "Load images to see estimated time and capacity.";
            this.CapacityWarningText = string.Empty;
        }

        if (this.TryGetImageData(this.StegoInputPath, out var stegoImage, out var stegoFileSize) && stegoImage is not null)
        {
            this.StegoInfoText = $"Stego: {stegoImage.Width}×{stegoImage.Height} | File: {FormatBytes(stegoFileSize)}";
        }
        else
        {
            this.StegoInfoText = "Select the stego image for extraction.";
        }

        if (this.ResultPreview is null)
        {
            this.ExtractInfoText = "Select the stego image and the XOR key, then extract.";
        }
    }

    /// <summary>
    /// Returns the default output folder used by the application.
    /// It prefers the user's Downloads folder and falls back to Desktop when needed.
    /// </summary>
    /// <returns>Default folder path for saved PNG output files.</returns>

    private static string GetDefaultOutputFolderPath()
    {
        var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var downloadsPath = Path.Combine(userProfilePath, "Downloads");

        if (Directory.Exists(downloadsPath))
        {
            return downloadsPath;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }

    /// <summary>
    /// Normalizes image input paths so only PNG files are accepted by the image selectors.
    /// Empty input remains empty, while non-PNG values are ignored.
    /// </summary>
    /// <param name="path">Raw image path entered or selected by the user.</param>
    /// <returns>The original path when it points to a PNG file; otherwise an empty string.</returns>

    private static string NormalizePngInputPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? path
            : string.Empty;
    }

    /// <summary>
    /// Builds GA parameters without interrupting normal UI refresh logic.
    /// If parsing fails, it falls back to safe defaults so the screen can still update.
    /// </summary>
    /// <returns>A validated parameter object or a safe default fallback.</returns>

    private GaParameters BuildValidatedParametersSafe()
    {
        try
        {
            return this.BuildValidatedParameters();
        }
        catch
        {
            return new GaParameters();
        }
    }

    /// <summary>
    /// Tries to load PNG information for a given path.
    /// Returns false instead of throwing so the UI can show friendly feedback while the user is still editing fields.
    /// </summary>
    /// <param name="path">Image path to inspect.</param>
    /// <param name="image">Loaded image when successful; otherwise <c>null</c>.</param>
    /// <param name="fileSize">PNG file size in bytes when successful.</param>
    /// <returns><c>true</c> when the file exists and was loaded successfully.</returns>

    private bool TryGetImageData(string path, out PngImageData? image, out long fileSize)
    {
        image = null;
        fileSize = 0;

        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            fileSize = new FileInfo(path).Length;
            image = this.imageIoService.LoadPng(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Builds the final output file path from the selected folder and file name.
    /// Also normalizes the extension to PNG when needed.
    /// </summary>
    /// <returns>The full output path that will be used for saving the result.</returns>

    private string BuildOutputPath()
    {
        if (string.IsNullOrWhiteSpace(this.OutputFolderPath))
        {
            throw new InvalidOperationException("Output folder path is required.");
        }

        if (string.IsNullOrWhiteSpace(this.OutputFileName))
        {
            throw new InvalidOperationException("Output file name is required.");
        }

        var trimmedName = this.OutputFileName.Trim();
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            if (trimmedName.Contains(invalidChar))
            {
                throw new InvalidOperationException("Output file name contains invalid characters.");
            }
        }

        var finalName = trimmedName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? trimmedName
            : $"{trimmedName}.png";

        Directory.CreateDirectory(this.OutputFolderPath);
        return Path.Combine(this.OutputFolderPath, finalName);
    }

    /// <summary>
    /// Loads an image preview for the UI without locking the source file.
    /// </summary>
    /// <param name="path">Path of the image to preview.</param>
    /// <returns>A frozen <see cref="ImageSource"/> for binding, or <c>null</c> when loading fails.</returns>

    private static ImageSource? LoadPreview(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            var bitmap = new BitmapImage();
            using var stream = File.OpenRead(path);
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Formats a byte count into a short human-readable string such as KB or MB.
    /// </summary>
    /// <param name="value">Number of bytes to format.</param>
    /// <returns>A short text such as B, KB, MB, or GB.</returns>

    private static string FormatBytes(long value)
    {
        // 1024 is the binary step between storage units: B -> KB -> MB -> GB.
        const double scale = 1024.0;
        string[] units = ["B", "KB", "MB", "GB"];

        double size = value;
        var unitIndex = 0;
        while (size >= scale && unitIndex < units.Length - 1)
        {
            size /= scale;
            unitIndex++;
        }

        return $"{size:0.##} {units[unitIndex]}";
    }

    /// <summary>
    /// Formats a duration into a short text that is easy to display in the UI.
    /// </summary>
    /// <param name="value">Duration to format.</param>
    /// <returns>A short readable duration string.</returns>

    private static string FormatDuration(TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
        {
            return "0s";
        }

        if (value.TotalHours >= 1)
        {
            return $"{(int)value.TotalHours}h {value.Minutes}m {value.Seconds}s";
        }

        if (value.TotalMinutes >= 1)
        {
            return $"{value.Minutes}m {value.Seconds}s";
        }

        if (value.TotalSeconds >= 1)
        {
            return $"{value.TotalSeconds:0.#}s";
        }

        return $"{value.TotalMilliseconds:0} ms";
    }

    /// <summary>
    /// Creates the standard PNG open-file dialog used by the browse commands.
    /// </summary>
    /// <returns>A configured file dialog that accepts PNG files only.</returns>

    private static Microsoft.Win32.OpenFileDialog CreateOpenPngDialog()
    {
        return new Microsoft.Win32.OpenFileDialog
        {
            Filter = "PNG images (*.png)|*.png",
            CheckFileExists = true,
            Multiselect = false
        };
    }

    /// <summary>
    /// Forces WPF to re-evaluate command availability.
    /// This keeps buttons enabled or disabled according to the current busy state.
    /// </summary>

    private void RefreshCommands()
    {
        this.SetEmbedModeCommand.RaiseCanExecuteChanged();
        this.SetExtractModeCommand.RaiseCanExecuteChanged();
        this.BrowsePayloadCommand.RaiseCanExecuteChanged();
        this.BrowseCoverCommand.RaiseCanExecuteChanged();
        this.BrowseStegoInputCommand.RaiseCanExecuteChanged();
        this.BrowseOutputFolderCommand.RaiseCanExecuteChanged();
        this.EmbedCommand.RaiseCanExecuteChanged();
        this.ExtractCommand.RaiseCanExecuteChanged();
    }
}
