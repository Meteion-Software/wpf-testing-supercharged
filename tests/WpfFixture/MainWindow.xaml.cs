using System.Diagnostics;
using System.Windows;

namespace WpfFixture;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var automated = false;
        var debug = false;
#if AUTOMATED_TEST
        automated = true;
#endif
#if DEBUG
        debug = true;
#endif
        ModeText.Text = $"automated={automated};debug={debug}";

        Console.WriteLine("fixture stdout ready");
        Console.Error.WriteLine("fixture stderr ready");
        Debug.WriteLine("fixture debug ready");
    }
}
