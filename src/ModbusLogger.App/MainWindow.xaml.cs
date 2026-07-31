using System.Collections.Specialized;
using System.Windows;
using System.Windows.Threading;
using ModbusLogger.App.ViewModels;

namespace ModbusLogger.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private bool _scrollPending;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(Dispatcher);
        DataContext = _vm;
        _vm.LogLines.CollectionChanged += LogLines_CollectionChanged;
        Closing += (_, _) => _vm.Shutdown();
    }

    private void LogLines_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // ScrollIntoView sili takojšnjo postavitev; klican neposredno tu bi se pri več
        // zaporednih vrsticah dnevnika (npr. med zagonom) prekrival z obdelavo prejšnjega
        // dogodka in podrl ItemContainerGenerator. Zato odložimo na konec trenutnega batcha.
        if (_scrollPending)
            return;
        _scrollPending = true;
        Dispatcher.BeginInvoke(() =>
        {
            _scrollPending = false;
            if (LogList.Items.Count > 0)
                LogList.ScrollIntoView(LogList.Items[^1]);
        }, DispatcherPriority.Background);
    }
}
