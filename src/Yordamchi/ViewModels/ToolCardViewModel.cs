using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yordamchi.Models;

namespace Yordamchi.ViewModels;

/// <summary>
/// Bosh sahifadagi bitta vosita kartochkasi: <see cref="ToolDescriptor"/> dagi statik
/// ma'lumotni UI ga qulay ko'rinishda ochadi va bosilganda ishchi oynani ochadi.
/// </summary>
public sealed partial class ToolCardViewModel : ObservableObject
{
    private readonly Action<ToolDescriptor> _onOpen;

    public ToolCardViewModel(ToolDescriptor tool, Action<ToolDescriptor> onOpen)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(onOpen);

        Tool = tool;
        _onOpen = onOpen;
        AccentBrush = CreateBrush(tool.AccentColor);
    }

    public ToolDescriptor Tool { get; }

    public string Title => Tool.Title;

    public string Description => Tool.Description;

    /// <summary>Segoe Fluent Icons shriftidagi belgi.</summary>
    public string Glyph => Tool.Glyph;

    /// <summary>
    /// Kartochka ikonasi foni. Muzlatilgan (frozen) — shuning uchun uni istalgan oqimdan
    /// ishlatish mumkin va WPF uni tezroq chizadi.
    /// </summary>
    public Brush AccentBrush { get; }

    /// <summary>Qidiruvda solishtiriladigan matn: nom + izoh + bo'lim nomi.</summary>
    public string SearchIndex => $"{Tool.Title} {Tool.Description} {Tool.CategoryTitle}";

    [RelayCommand]
    private void Open() => _onOpen(Tool);

    private static Brush CreateBrush(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch (FormatException)
        {
            // Katalogda noto'g'ri HEX yozilib qolsa, dastur ishdan chiqmasin.
            return Brushes.Gray;
        }
    }

    public override string ToString() => Title;
}

/// <summary>
/// Bosh sahifadagi bitta bo'lim ("Sahifalar bilan ishlash", "Konvertatsiya" …).
/// Qidiruv paytida <see cref="Tools"/> filtrlanadi; bo'lim bo'sh qolsa
/// <see cref="HasTools"/> orqali butunlay yashiriladi.
/// </summary>
public sealed partial class ToolGroupViewModel : ObservableObject
{
    /// <summary>Filtrdan qat'i nazar guruhga tegishli barcha kartochkalar — asl tartibda.</summary>
    private readonly IReadOnlyList<ToolCardViewModel> _allTools;

    public ToolGroupViewModel(string title, string glyph, IEnumerable<ToolCardViewModel> tools)
    {
        Title = title;
        Glyph = glyph;
        _allTools = tools.ToList();
        Tools = new ObservableCollection<ToolCardViewModel>(_allTools);
    }

    public string Title { get; }

    /// <summary>Bo'lim sarlavhasi yonidagi ikonka.</summary>
    public string Glyph { get; }

    /// <summary>Hozir ko'rinib turgan kartochkalar (filtr natijasi).</summary>
    public ObservableCollection<ToolCardViewModel> Tools { get; }

    /// <summary>Filtrdan keyin guruhda kartochka qoldimi.</summary>
    public bool HasTools => Tools.Count > 0;

    /// <summary>
    /// Qidiruv matni bo'yicha kartochkalarni filtrlaydi. Bo'sh matn — barcha kartochkalar.
    /// </summary>
    /// <param name="query">Foydalanuvchi kiritgan matn; <c>null</c> yoki bo'sh bo'lishi mumkin.</param>
    /// <returns>Filtrdan keyin guruhda kamida bitta kartochka qolganmi.</returns>
    public bool ApplyFilter(string? query)
    {
        var matches = string.IsNullOrWhiteSpace(query)
            ? _allTools
            : _allTools.Where(tool => tool.SearchIndex.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToList();

        // Ro'yxatni butunlay almashtirmaymiz: mos keladigan elementlarni joyida qo'shib/olib
        // tashlaymiz, shunda WPF faqat o'zgargan kartochkalarni qayta chizadi.
        for (var i = Tools.Count - 1; i >= 0; i--)
        {
            if (!matches.Contains(Tools[i]))
                Tools.RemoveAt(i);
        }

        var index = 0;
        foreach (var tool in matches)
        {
            if (index >= Tools.Count || !ReferenceEquals(Tools[index], tool))
                Tools.Insert(index, tool);

            index++;
        }

        OnPropertyChanged(nameof(HasTools));
        return HasTools;
    }

    public override string ToString() => $"{Title} ({Tools.Count})";
}
