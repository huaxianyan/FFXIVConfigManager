using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXIVConfigManager.Desktop.Localization;
using FFXIVConfigManager.Desktop.Services;
using FFXIVConfigManager.Domain.Portraits;

namespace FFXIVConfigManager.Desktop.ViewModels;

public sealed partial class PortraitBackupEditViewModel : ViewModelBase
{
    public PortraitBackupEditViewModel(
        string schemeName,
        string note,
        ITextLocalizer text)
    {
        SchemeName = schemeName;
        Note = note;
        Title = text["EditPortraitBackupSchemeTitle"];
    }

    public event Action<PortraitBackupEditResult?>? CloseRequested;

    public string Title { get; }

    public int MaximumSchemeNameLength => PortraitBackupManifest.MaximumSchemeNameLength;

    public int MaximumNoteLength => PortraitBackupManifest.MaximumNoteLength;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string SchemeName { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string Note { get; set; }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => CloseRequested?.Invoke(new PortraitBackupEditResult(
        SchemeName.Trim(),
        Note.Trim()));

    private bool CanSave() =>
        !string.IsNullOrWhiteSpace(SchemeName) &&
        SchemeName.Trim().Length <= MaximumSchemeNameLength &&
        !string.IsNullOrWhiteSpace(Note) &&
        Note.Trim().Length <= MaximumNoteLength;
}
