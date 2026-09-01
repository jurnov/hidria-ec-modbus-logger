using System.Windows;
using ModbusLogger.App.ViewModels;

namespace ModbusLogger.App;

/// <summary>Nemodalno okno — DataContext je kar glavni MainViewModel, nastavitve so že live-bound.</summary>
public partial class LoggingSettingsWindow : Window
{
    public LoggingSettingsWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        MySqlPasswordBox.Password = vm.MySqlPassword;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void MySqlPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.MySqlPassword = MySqlPasswordBox.Password;
    }
}
