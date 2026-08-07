using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Yordamchi.ViewModels;

/// <summary>
/// "Sanoq sistemasi" bo'limidagi jadvalning bitta qatori: bitta asos va shu asosdagi natija.
/// <para>
/// Qatorlar sahifa ochilganda <b>bir marta</b> yaratiladi va keyin faqat qiymati yangilanadi.
/// Har bosishda 31 ta yangi obyekt yasash WPF ni ro'yxatni qaytadan qurishga majbur qilardi
/// va tanlov ham yo'qolib ketardi.
/// </para>
/// </summary>
public sealed partial class NumberBaseRowViewModel : ObservableObject
{
    private readonly Action<NumberBaseRowViewModel> _copy;
    private readonly Action<NumberBaseRowViewModel> _select;

    public NumberBaseRowViewModel(
        int radix,
        string name,
        bool isPopular,
        Action<NumberBaseRowViewModel> copy,
        Action<NumberBaseRowViewModel> select)
    {
        Base = radix;
        Name = name;
        IsPopular = isPopular;
        _copy = copy;
        _select = select;
    }

    /// <summary>Sanoq sistemasi asosi: 2 dan 32 gacha.</summary>
    public int Base { get; }

    /// <summary>Asosning o'zbekcha nomi, masalan "o'n oltilik".</summary>
    public string Name { get; }

    /// <summary>Eng ko'p ishlatiladigan asos (2, 8, 10, 16) — qator ajratib ko'rsatiladi.</summary>
    public bool IsPopular { get; }

    public string BaseText => Base.ToString(CultureInfo.InvariantCulture);

    /// <summary>Toza natija — nusxa olishda va teskari o'tkazishda aynan shu ishlatiladi.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValue))]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand))]
    private string _rawValue = string.Empty;

    /// <summary>Ekranda ko'rinadigan (kerak bo'lsa guruhlangan) ko'rinish.</summary>
    [ObservableProperty]
    private string _displayValue = string.Empty;

    /// <summary>Kasr qism cheksiz bo'lgani uchun kesilgan — qator yonida "≈" ko'rinadi.</summary>
    [ObservableProperty]
    private bool _isApproximate;

    /// <summary>Qadam-baqadam yechim va "almashtirish" shu qator uchun ishlaydi.</summary>
    [ObservableProperty]
    private bool _isSelected;

    public bool HasValue => RawValue.Length > 0;

    public void Update(string value, bool isExact, string displayValue)
    {
        RawValue = value;
        DisplayValue = displayValue;
        IsApproximate = !isExact;
    }

    public void Clear()
    {
        RawValue = string.Empty;
        DisplayValue = string.Empty;
        IsApproximate = false;
    }

    [RelayCommand(CanExecute = nameof(HasValue))]
    private void Copy() => _copy(this);

    [RelayCommand]
    private void Select() => _select(this);

    public override string ToString() => $"{BaseText}: {RawValue}";
}
