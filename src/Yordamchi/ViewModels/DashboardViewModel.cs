using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yordamchi.Models;
using Yordamchi.Services.Abstractions;

namespace Yordamchi.ViewModels;

/// <summary>
/// Bosh sahifa: barcha vositalar bo'limlarga ajratilgan kartochkalar ko'rinishida.
/// <para>
/// Bu yerda hech qanday PDF mantiq yo'q — sahifa faqat <see cref="ToolCatalog"/> ni ko'rsatadi
/// va foydalanuvchi tanlagan vositani <see cref="ToolSelected"/> hodisasi orqali qobiqqa uzatadi.
/// Qaysi ishchi oyna ochilishini shell (MainViewModel) hal qiladi.
/// </para>
/// </summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    public DashboardViewModel(IDialogService dialogService)
        : base(dialogService)
    {
        Groups = new ObservableCollection<ToolGroupViewModel>(BuildGroups());
    }

    // Yon paneldagi yorliq shu Title dan olinadi. Dastur nomi ("Yordamchi") panelning
    // tepasida allaqachon yozilgan, shuning uchun bo'lim aniq nomlanadi.
    public override string Title => "PDF vositalari";

    public override string Description =>
        "PDF bilan ishlash uchun kerak bo'lgan hamma narsa — bitta dasturda, internetsiz.";

    /// <summary>Bosh sahifadagi katta sarlavha.</summary>
    public string HeadlineText => "Barcha PDF vositalari";

    /// <summary>Kartochka bosilganda ko'tariladi; qobiq shu vosita uchun ishchi oynani ochadi.</summary>
    public event EventHandler<ToolDescriptor>? ToolSelected;

    /// <summary>Bo'limlar — <see cref="ToolCategory"/> tartibida.</summary>
    public ObservableCollection<ToolGroupViewModel> Groups { get; }

    /// <summary>Qidiruv maydonidagi matn; har bir belgida guruhlar filtrlanadi.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchText))]
    private string _searchText = string.Empty;

    /// <summary>Qidiruvda hech narsa topilmadi — bo'sh holat paneli shu bilan boshqariladi.</summary>
    [ObservableProperty]
    private bool _hasNoResults;

    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

    /// <summary>Katalogdagi vositalar soni — sarlavha ostidagi izohda ko'rsatiladi.</summary>
    public int ToolCount => ToolCatalog.All.Count;

    partial void OnSearchTextChanged(string value) => ApplyFilter(value);

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    /// <summary>Kartochkalar shu metodni chaqiradi (konstruktorga <c>onOpen</c> sifatida berilgan).</summary>
    private void OpenTool(ToolDescriptor tool) => ToolSelected?.Invoke(this, tool);

    private void ApplyFilter(string? query)
    {
        var anyVisible = false;
        foreach (var group in Groups)
            anyVisible |= group.ApplyFilter(query);

        HasNoResults = !anyVisible;
    }

    private IEnumerable<ToolGroupViewModel> BuildGroups()
    {
        // Katalogdagi tartib ataylab saqlanadi: u bosh sahifadagi mantiqiy ketma-ketlik.
        return ToolCatalog.All
            .GroupBy(tool => tool.Category)
            .OrderBy(group => (int)group.Key)
            .Select(group => new ToolGroupViewModel(
                group.First().CategoryTitle,
                DescribeCategory(group.Key),
                group.Select(tool => new ToolCardViewModel(tool, OpenTool))));
    }

    /// <summary>Bo'lim sarlavhasi yonidagi ikonka (Segoe Fluent Icons belgi kodi).</summary>
    private static string DescribeCategory(ToolCategory category) => category switch
    {
        ToolCategory.Pages => "\uE7C3",      // Page
        ToolCategory.Convert => "\uE8AB",    // Switch
        ToolCategory.Optimize => "\uE72E",   // Lock
        ToolCategory.Ai => "\uE945",         // Lightning
        _ => "\uE700"                        // GlobalNavButton
    };
}
