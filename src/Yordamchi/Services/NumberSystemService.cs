using Yordamchi.Models;
using Yordamchi.Services.Abstractions;
using Yordamchi.Services.Conversion;

namespace Yordamchi.Services;

/// <summary>
/// <inheritdoc cref="INumberSystemService"/>
/// <para>
/// Butun mantiq <see cref="NumberBaseConverter"/> da — bu sinf uni shartnoma ortiga yashiradi,
/// ya'ni <c>ViewModels</c> qatlami implementatsiyaga emas, interfeysga tayanadi va sinovda
/// almashtirilishi mumkin.
/// </para>
/// </summary>
public sealed class NumberSystemService : INumberSystemService
{
    public int MinBase => NumberBaseConverter.MinBase;

    public int MaxBase => NumberBaseConverter.MaxBase;

    public IReadOnlyList<int> SupportedBases => NumberBaseConverter.SupportedBases;

    public IReadOnlyList<int> PopularBases => NumberBaseConverter.PopularBases;

    public bool IsSupportedBase(int radix) => NumberBaseConverter.IsSupportedBase(radix);

    public bool UsesDigitGroups(int radix) => NumberBaseConverter.UsesDigitGroups(radix);

    public string DescribeBase(int radix) => NumberBaseConverter.DescribeBase(radix);

    public string LabelBase(int radix) => NumberBaseConverter.LabelBase(radix);

    public string DigitsOf(int radix) => NumberBaseConverter.DigitsOf(radix);

    public string? Validate(string? text, int fromBase) => NumberBaseConverter.Validate(text, fromBase);

    public NumberConversionResult Convert(string? text, int fromBase, int toBase, int fractionDigits)
        => NumberBaseConverter.Convert(text, fromBase, toBase, fractionDigits);

    public IReadOnlyList<ConversionExplanationSection> Explain(string? text, int fromBase, int toBase, int fractionDigits)
        => NumberBaseConverter.Explain(text, fromBase, toBase, fractionDigits);

    public string Group(string? value, int radix) => NumberBaseConverter.Group(value, radix);
}
