using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.ViewModels;

public partial class MemberEditViewModel : ObservableObject
{
    internal Func<OpenFileDialog>? PhotoDialogFactory { get; set; }
    internal Func<IDisposable>? ModalOperationFactory { get; set; }
    [ObservableProperty] private int _id;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private Gender _gender = Gender.Male;
    [ObservableProperty] private string _birthDate = string.Empty;
    [ObservableProperty] private string? _deathDate;
    [ObservableProperty] private string? _spouseName;
    [ObservableProperty] private string? _biography;
    [ObservableProperty] private string? _photoPath;
    [ObservableProperty] private int _generation = 1;
    [ObservableProperty] private int _birthOrder;
    [ObservableProperty] private bool _isNew;

    public MemberEditViewModel()
    {
    }

    public MemberEditViewModel(FamilyMember member)
    {
        LoadFromMember(member);
    }

    public void LoadFromMember(FamilyMember member)
    {
        Id = member.Id;
        Name = member.Name;
        Gender = member.Gender;
        BirthDate = member.BirthDate;
        DeathDate = member.DeathDate;
        SpouseName = member.SpouseName;
        Biography = member.Biography;
        PhotoPath = member.PhotoPath;
        Generation = member.Generation;
        BirthOrder = member.BirthOrder;
    }

    public FamilyMember ToMember()
    {
        return new FamilyMember
        {
            Id = Id,
            Name = Name,
            Gender = Gender,
            BirthDate = BirthDate,
            DeathDate = DeathDate,
            SpouseName = SpouseName,
            Biography = Biography,
            PhotoPath = PhotoPath,
            Generation = Generation,
            BirthOrder = BirthOrder
        };
    }

    public void ApplyToMember(FamilyMember member)
    {
        member.Name = Name;
        member.Gender = Gender;
        member.BirthDate = BirthDate;
        member.DeathDate = DeathDate;
        member.SpouseName = SpouseName;
        member.Biography = Biography;
        member.PhotoPath = PhotoPath;
        member.Generation = Generation;
        member.BirthOrder = BirthOrder;
    }

    [RelayCommand]
    private void BrowsePhoto()
    {
        IDisposable? operation;
        try { operation = ModalOperationFactory?.Invoke(); }
        catch (InvalidOperationException) { return; }
        using var operationLease = operation;
        var dialog = PhotoDialogFactory?.Invoke() ?? new OpenFileDialog
        {
            Title = "选择照片",
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif|所有文件|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            PhotoPath = dialog.FileName;
        }
    }
}
