using System.Windows;
using ImageLocker.Services;
using ImageLocker.ViewModels;

namespace ImageLocker.Views;

/// <summary>
/// Main WPF window for the application.
/// </summary>
public partial class MainWindow : Window
{

    /// <summary>
    /// Creates the window, builds the service graph, and assigns the main view model as the DataContext.
    /// </summary>
    /// <remarks>
    /// The project currently wires dependencies manually in code-behind instead of using a separate DI container.
    /// </remarks>
    public MainWindow()
    {
        InitializeComponent();

        var imageIo = new ImageIOService();
        var headerService = new BinaryHeaderService();
        var bitStreamService = new BitStreamService();
        var xorCipherService = new XorCipherService();
        var lsbService = new LsbSteganographyService();
        var metricsService = new MetricsService();
        var crc32Service = new Crc32Service();
        var gaService = new GeneticAlgorithmService(lsbService, metricsService);

        var coordinator = new SteganographyCoordinator(
            imageIo,
            headerService,
            bitStreamService,
            xorCipherService,
            lsbService,
            metricsService,
            gaService,
            crc32Service);

        this.DataContext = new MainViewModel(coordinator, imageIo);
    }
}
