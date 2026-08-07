using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yordamchi.Models;
using Yordamchi.Services.Abstractions;

namespace Yordamchi.ViewModels;

/// <summary>Asos tanlash ro'yxatining bitta bandi.</summary>
/// <param name="Value">Asos: 2 dan 32 gacha.</param>
/// <param name="Label">Ko'rinadigan yorliq: "16-lik — o'n oltilik".</param>
public sealed record NumberBaseChoice(int Value, string Label);

/// <summary>
/// "Sanoq sistemasi" bo'limi: tepada son va uning asosi kiritiladi, pastda esa natija
/// <b>barcha</b> asoslarda (2 dan 32 gacha) bir vaqtning o'zida ko'rinadi.
/// <para>
/// Hisob har bosishda darhol bajariladi — "Hisoblash" tugmasi yo'q. Buning uchun ish sinxron
/// bo'lishi shart, shuning uchun bu sahifa <c>ViewModelBase.RunAsync</c> ni ishlatmaydi:
/// 31 ta o'tkazish mikrosoniyalarda tugaydi va "band" qoplamasi faqat xalaqit berardi.
/// </para>
/// </summary>
public sealed partial class NumberSystemViewModel : ViewModelBase
{
    private readonly INumberSystemService _numbers;
    private readonly List<NumberBaseRowViewModel> _allRows = [];

    public NumberSystemViewModel(INumberSystemService numbers, IDialogService dialogService)
        : base(dialogService)
    {
        _numbers = numbers;

        BaseChoices = [.. numbers.SupportedBases.Select(radix => new NumberBaseChoice(radix, numbers.LabelBase(radix)))];
        QuickBases = numbers.PopularBases;

        foreach (var radix in numbers.SupportedBases)
        {
            _allRows.Add(new NumberBaseRowViewModel(
                radix,
                numbers.DescribeBase(radix),
                numbers.PopularBases.Contains(radix),
                CopyRow,
                SelectRow));
        }

        ApplyFilter();
        UpdateSelection();
        Recalculate();
    }

    public override string Title => "Sanoq sistemasi";

    public override string Description =>
        "Sonni 2 dan 32 gacha bo'lgan istalgan asosga o'tkazing — natija barcha sanoq sistemalarida bir vaqtda ko'rinadi.";

    // =================================================================================
    //  Kiritish
    // =================================================================================

    /// <summary>Asos tanlash ro'yxati (2–32).</summary>
    public IReadOnlyList<NumberBaseChoice> BaseChoices { get; }

    /// <summary>Tepadagi tezkor tugmalar: 2, 8, 10, 16.</summary>
    public IReadOnlyList<int> QuickBases { get; }

    /// <summary>Kiritilgan son qaysi sanoq sistemasida yozilgan.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SourceBaseName))]
    [NotifyPropertyChangedFor(nameof(AllowedDigits))]
    private int _sourceBase = 10;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private string _sourceText = string.Empty;

    /// <summary>Kiritilgan son asosga mos kelmasa — tushunarli xabar; aks holda <c>null</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public string SourceBaseName => _numbers.DescribeBase(SourceBase);

    /// <summary>Kiritish maydoni ostidagi eslatma: shu asosda qaysi belgilar ishlatiladi.</summary>
    public string AllowedDigits =>
        $"{SourceBase.ToString(CultureInfo.InvariantCulture)}-lik sanoq sistemasi belgilari: {_numbers.DigitsOf(SourceBase)}. "
        + "Kasr qismini nuqta yoki vergul bilan ajrating.";

    partial void OnSourceTextChanged(string value) => Recalculate();

    partial void OnSourceBaseChanged(int value)
    {
        // Manba asosi nishon bilan ustma-ust tushib qolmasin: bunday holda tushuntirish
        // "o'tkazish kerak emas" bo'lib qolardi va sahifa foydasiz ko'rinardi.
        if (SelectedBase == value)
            SelectedBase = value == 10 ? 2 : 10;

        Recalculate();
    }

    /// <summary>
    /// Tezkor tugmalar (2 · 8 · 10 · 16) uchun. Parametr XAML dan son bo'lib ham, satr bo'lib
    /// ham kelishi mumkin — ikkalasi ham qabul qilinadi.
    /// </summary>
    [RelayCommand]
    private void SetSourceBase(object? value)
    {
        var radix = value switch
        {
            int number => number,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0
        };

        if (radix >= _numbers.MinBase && radix <= _numbers.MaxBase)
            SourceBase = radix;
    }

    [RelayCommand(CanExecute = nameof(HasSourceText))]
    private void Clear()
    {
        SourceText = string.Empty;
        StatusMessage = string.Empty;
    }

    private bool HasSourceText() => !string.IsNullOrEmpty(SourceText);

    // =================================================================================
    //  Sozlamalar
    // =================================================================================

    /// <summary>Kasr qism uchun ko'rsatiladigan xonalar soni.</summary>
    [ObservableProperty]
    private int _fractionDigits = 16;

    public IReadOnlyList<int> FractionDigitChoices { get; } = [8, 12, 16, 24, 32];

    /// <summary>Uzun natijalarni o'qishga qulay qilib guruhlash (faqat ko'rinishga ta'sir qiladi).</summary>
    [ObservableProperty]
    private bool _groupDigits = true;

    /// <summary>Ro'yxatda faqat 2, 8, 10 va 16 qolsin.</summary>
    [ObservableProperty]
    private bool _onlyPopularBases;

    partial void OnFractionDigitsChanged(int value) => Recalculate();

    partial void OnGroupDigitsChanged(bool value) => Recalculate();

    partial void OnOnlyPopularBasesChanged(bool value) => ApplyFilter();

    // =================================================================================
    //  Natijalar
    // =================================================================================

    /// <summary>Ekranda ko'rinadigan qatorlar — filtr shu ro'yxatni qaytadan to'ldiradi.</summary>
    public ObservableCollection<NumberBaseRowViewModel> Rows { get; } = [];

    /// <summary>Qadam-baqadam yechim va "almashtirish" ishlaydigan asos.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedBaseName))]
    private int _selectedBase = 2;

    public string SelectedBaseName => _numbers.DescribeBase(SelectedBase);

    public NumberBaseRowViewModel? SelectedRow => _allRows.FirstOrDefault(row => row.Base == SelectedBase);

    /// <summary>Tanlangan asosdagi toza natija.</summary>
    public string SelectedValue => SelectedRow?.RawValue ?? string.Empty;

    public string SelectedDisplayValue => SelectedRow?.DisplayValue ?? string.Empty;

    public bool HasResult => SelectedValue.Length > 0;

    /// <summary>Tanlangan natija kesilgan — teskari o'tkazishda kichik farq bo'lishi mumkin.</summary>
    public bool IsSelectedApproximate => SelectedRow?.IsApproximate ?? false;

    partial void OnSelectedBaseChanged(int value)
    {
        UpdateSelection();
        RefreshExplanation();
        RefreshSelectedResult();
    }

    private void SelectRow(NumberBaseRowViewModel row) => SelectedBase = row.Base;

    private void UpdateSelection()
    {
        foreach (var row in _allRows)
            row.IsSelected = row.Base == SelectedBase;
    }

    private void ApplyFilter()
    {
        Rows.Clear();

        foreach (var row in _allRows)
        {
            if (!OnlyPopularBases || row.IsPopular)
                Rows.Add(row);
        }
    }

    // =================================================================================
    //  Qadam-baqadam yechim
    // =================================================================================

    public ObservableCollection<ConversionExplanationSection> Explanation { get; } = [];

    public bool HasExplanation => Explanation.Count > 0;

    private void RefreshExplanation()
    {
        Explanation.Clear();

        if (!HasError && !string.IsNullOrWhiteSpace(SourceText))
        {
            foreach (var section in _numbers.Explain(SourceText, SourceBase, SelectedBase, FractionDigits))
                Explanation.Add(section);
        }

        OnPropertyChanged(nameof(HasExplanation));
    }

    // =================================================================================
    //  Amallar
    // =================================================================================

    /// <summary>
    /// Tanlangan natijani kiritish maydoniga ko'chiradi va asoslarni o'rin almashtiradi —
    /// o'tkazishni darhol teskari yo'nalishda tekshirish uchun eng qisqa yo'l.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasResult))]
    private void Swap()
    {
        var row = SelectedRow;

        if (row is null || !row.HasValue)
            return;

        var approximate = row.IsApproximate;
        var value = row.RawValue;
        var previousBase = SourceBase;

        SelectedBase = previousBase;
        SourceBase = row.Base;
        SourceText = value;

        StatusMessage = approximate
            ? "Almashtirildi. Diqqat: natija kesilgan edi, shuning uchun qaytish qiymati bir oz farq qilishi mumkin."
            : $"Almashtirildi: endi son {row.Base.ToString(CultureInfo.InvariantCulture)}-lik sanoq sistemasida o'qiladi.";
    }

    [RelayCommand(CanExecute = nameof(HasResult))]
    private void CopySelected()
    {
        if (SelectedRow is not null)
            CopyRow(SelectedRow);
    }

    private void CopyRow(NumberBaseRowViewModel row)
    {
        if (!row.HasValue)
            return;

        // Guruhlash faqat ko'rinish uchun — nusxaga toza qiymat ketadi.
        DialogService.SetClipboardText(row.RawValue);

        StatusMessage = $"{row.Base.ToString(CultureInfo.InvariantCulture)}-lik natija nusxa olindi: {row.RawValue}";
    }

    // =================================================================================
    //  Hisob
    // =================================================================================

    private void Recalculate()
    {
        ErrorMessage = _numbers.Validate(SourceText, SourceBase);

        var ready = !HasError && !string.IsNullOrWhiteSpace(SourceText);

        foreach (var row in _allRows)
        {
            if (!ready)
            {
                row.Clear();
                continue;
            }

            var result = _numbers.Convert(SourceText, SourceBase, row.Base, FractionDigits);

            row.Update(
                result.Value,
                result.IsExact,
                GroupDigits ? _numbers.Group(result.Value, row.Base) : result.Value);
        }

        RefreshExplanation();
        RefreshSelectedResult();
    }

    private void RefreshSelectedResult()
    {
        OnPropertyChanged(nameof(SelectedRow));
        OnPropertyChanged(nameof(SelectedValue));
        OnPropertyChanged(nameof(SelectedDisplayValue));
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(IsSelectedApproximate));

        SwapCommand.NotifyCanExecuteChanged();
        CopySelectedCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }
}
