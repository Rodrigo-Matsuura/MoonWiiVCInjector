using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Moon_WiiVC_Injector.Services;
using Moon_WiiVC_Injector.ViewModels;

namespace Moon_WiiVC_Injector;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();

        var dialogService = new AvaloniaDialogService(() => this);
        ViewModel = new MainViewModel(dialogService);
        DataContext = ViewModel;

        SetupDragAndDrop();
    }

    private void SetupDragAndDrop()
    {
        AddHandler(DragDrop.DragOverEvent, OnWindowDragOver);
        AddHandler(DragDrop.DropEvent, OnWindowDrop);
    }

    private void OnWindowDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Items.Count > 0)
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private async void OnWindowDrop(object? sender, DragEventArgs e)
    {
        IEnumerable<IStorageItem>? files = null;
        if (e.DataTransfer is IAsyncDataTransfer asyncDt)
        {
            files = await asyncDt.TryGetFilesAsync();
        }

        if (files == null) return;

        var paths = files.Select(f => f.Path.LocalPath).ToList();
        await ViewModel.LoadDroppedFilesAsync(paths);
    }

    private void OnKeyTextBoxDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && !string.IsNullOrEmpty(textBox.Name))
        {
            ViewModel.UnlockKey(textBox.Name);
        }
    }
}
