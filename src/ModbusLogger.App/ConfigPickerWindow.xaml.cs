using System.Windows;
using ModbusLogger.App.ViewModels;

namespace ModbusLogger.App;

public partial class ConfigPickerWindow : Window
{
    public ConfigPickerWindow(ConfigPickerViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.RequestClose += success =>
        {
            DialogResult = success;
            Close();
        };
    }
}
