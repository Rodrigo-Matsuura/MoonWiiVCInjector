using Avalonia.Controls;
using Moon_WiiVC_Injector.Services;
using Moon_WiiVC_Injector.ViewModels;

namespace Moon_WiiVC_Injector;

public partial class SdCardMenuAvalonia : Window
{
    public SdCardMenuAvalonia()
    {
        InitializeComponent();
        var dialogService = new AvaloniaDialogService(() => this);
        DataContext = new SdCardViewModel(dialogService);
    }
}
