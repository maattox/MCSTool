using CommunityToolkit.Mvvm.ComponentModel;

namespace McManager.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _greeting = "Welcome to Avalonia!";
}
