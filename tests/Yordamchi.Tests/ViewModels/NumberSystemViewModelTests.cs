using Yordamchi.Models;
using Yordamchi.Services;
using Yordamchi.Tests.TestSupport;
using Yordamchi.ViewModels;

namespace Yordamchi.Tests.ViewModels;

/// <summary>
/// "Sanoq sistemasi" sahifasi. Servis haqiqiy — u sof va tez, mock esa aynan tekshirilishi
/// kerak bo'lgan narsani (jadval to'g'ri to'ldiriladimi) yashirib qo'yardi.
/// </summary>
public sealed class NumberSystemViewModelTests
{
    private readonly FakeDialogService _dialogs = new();

    private NumberSystemViewModel CreateViewModel() => new(new NumberSystemService(), _dialogs);

    private static NumberBaseRowViewModel Row(NumberSystemViewModel vm, int radix)
        => vm.Rows.Single(row => row.Base == radix);

    // =================================================================================
    //  Jadval
    // =================================================================================

    [Fact]
    public void The_page_starts_with_every_base_from_two_to_thirty_two()
    {
        var vm = CreateViewModel();

        Assert.Equal(31, vm.Rows.Count);
        Assert.Equal(2, vm.Rows[0].Base);
        Assert.Equal(32, vm.Rows[^1].Base);
    }

    [Fact]
    public void Typing_a_number_fills_in_every_base_at_once()
    {
        var vm = CreateViewModel();

        vm.SourceText = "255";

        Assert.Equal("11111111", Row(vm, 2).RawValue);
        Assert.Equal("377", Row(vm, 8).RawValue);
        Assert.Equal("255", Row(vm, 10).RawValue);
        Assert.Equal("FF", Row(vm, 16).RawValue);
        Assert.Equal("7V", Row(vm, 32).RawValue);
    }

    [Fact]
    public void Changing_the_input_base_reads_the_same_text_differently()
    {
        var vm = CreateViewModel();
        vm.SourceText = "11";

        Assert.Equal("11", Row(vm, 10).RawValue);

        vm.SourceBase = 2;

        Assert.Equal("3", Row(vm, 10).RawValue);
    }

    [Fact]
    public void An_impossible_digit_stops_the_table_and_says_why()
    {
        var vm = CreateViewModel();
        vm.SourceBase = 2;

        vm.SourceText = "102";

        Assert.True(vm.HasError);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("«2»", vm.ErrorMessage);

        // Eski natijalar ekranda qolib ketmasligi kerak — ular endi yolg'on bo'lardi.
        Assert.All(vm.Rows, row => Assert.Equal(string.Empty, row.RawValue));
    }

    [Fact]
    public void Clearing_the_field_empties_the_table_without_an_error()
    {
        var vm = CreateViewModel();
        vm.SourceText = "255";

        vm.ClearCommand.Execute(null);

        Assert.Equal(string.Empty, vm.SourceText);
        Assert.False(vm.HasError);
        Assert.All(vm.Rows, row => Assert.Equal(string.Empty, row.RawValue));
    }

    [Fact]
    public void A_cut_fraction_is_marked_in_the_row()
    {
        var vm = CreateViewModel();

        vm.SourceText = "0.1";

        Assert.True(Row(vm, 2).IsApproximate);
        Assert.False(Row(vm, 10).IsApproximate);
    }

    // =================================================================================
    //  Ko'rinish sozlamalari
    // =================================================================================

    [Fact]
    public void Grouping_changes_only_what_is_shown()
    {
        var vm = CreateViewModel();
        vm.SourceText = "255";

        var binary = Row(vm, 2);

        Assert.Equal("1111 1111", binary.DisplayValue);
        Assert.Equal("11111111", binary.RawValue);

        vm.GroupDigits = false;

        Assert.Equal("11111111", binary.DisplayValue);
    }

    [Fact]
    public void The_fraction_length_setting_reaches_the_result()
    {
        var vm = CreateViewModel();
        vm.SourceText = "0.1";

        vm.FractionDigits = 8;

        Assert.Equal("0.00011001", Row(vm, 2).RawValue);
    }

    [Fact]
    public void The_filter_leaves_only_the_four_familiar_bases()
    {
        var vm = CreateViewModel();

        vm.OnlyPopularBases = true;

        Assert.Equal(new[] { 2, 8, 10, 16 }, vm.Rows.Select(row => row.Base));

        vm.OnlyPopularBases = false;

        Assert.Equal(31, vm.Rows.Count);
    }

    // =================================================================================
    //  Tanlov va qadam-baqadam yechim
    // =================================================================================

    [Fact]
    public void Clicking_a_row_moves_the_step_by_step_answer_to_it()
    {
        var vm = CreateViewModel();
        vm.SourceText = "255";

        Row(vm, 16).SelectCommand.Execute(null);

        Assert.Equal(16, vm.SelectedBase);
        Assert.True(Row(vm, 16).IsSelected);
        Assert.Equal("FF", vm.SelectedValue);

        // Belgilangan qator bittagina bo'lishi kerak.
        Assert.Single(vm.Rows, row => row.IsSelected);
    }

    [Fact]
    public void The_explanation_follows_the_selected_base()
    {
        var vm = CreateViewModel();
        vm.SourceText = "25";

        Assert.True(vm.HasExplanation);
        Assert.Equal("25₁₀ = 11001₂", vm.Explanation[^1].Summary);

        vm.SelectedBase = 16;

        Assert.Equal("25₁₀ = 19₁₆", vm.Explanation[^1].Summary);
    }

    [Fact]
    public void An_empty_field_has_nothing_to_explain()
    {
        var vm = CreateViewModel();

        Assert.False(vm.HasExplanation);
        Assert.Empty(vm.Explanation);
    }

    [Fact]
    public void The_target_never_collides_with_the_source_base()
    {
        // Aks holda yechim "o'tkazish kerak emas" bo'lib qolardi va sahifa foydasiz ko'rinardi.
        var vm = CreateViewModel();

        vm.SelectedBase = 16;
        vm.SourceBase = 16;

        Assert.NotEqual(vm.SourceBase, vm.SelectedBase);
    }

    // =================================================================================
    //  Amallar
    // =================================================================================

    [Fact]
    public void Swapping_feeds_the_result_back_as_the_new_input()
    {
        var vm = CreateViewModel();
        vm.SourceText = "255";
        vm.SelectedBase = 16;

        vm.SwapCommand.Execute(null);

        Assert.Equal(16, vm.SourceBase);
        Assert.Equal("FF", vm.SourceText);
        Assert.Equal(10, vm.SelectedBase);

        // Qiymat o'zgarmasligi kerak — faqat qaysi asosda o'qilishi o'zgardi.
        Assert.Equal("255", Row(vm, 10).RawValue);
    }

    [Fact]
    public void Swapping_a_cut_result_warns_that_it_is_not_reversible()
    {
        var vm = CreateViewModel();
        vm.SourceText = "0.1";
        vm.SelectedBase = 2;

        vm.SwapCommand.Execute(null);

        Assert.Contains("kesilgan", vm.StatusMessage);
    }

    [Fact]
    public void There_is_nothing_to_copy_or_swap_before_a_number_is_typed()
    {
        var vm = CreateViewModel();

        Assert.False(vm.HasResult);
        Assert.False(vm.SwapCommand.CanExecute(null));
        Assert.False(vm.CopySelectedCommand.CanExecute(null));
        Assert.False(vm.ClearCommand.CanExecute(null));
    }

    [Fact]
    public void Copying_hands_the_clean_value_to_the_shell()
    {
        var vm = CreateViewModel();
        vm.SourceText = "255";
        vm.SelectedBase = 2;

        vm.CopySelectedCommand.Execute(null);

        // Guruhlash faqat ko'rinish uchun: nusxaga bo'shliqsiz qiymat ketadi.
        Assert.Equal("11111111", Assert.Single(_dialogs.ClipboardTexts));
    }

    [Fact]
    public void A_row_can_be_copied_on_its_own()
    {
        var vm = CreateViewModel();
        vm.SourceText = "255";

        Row(vm, 16).CopyCommand.Execute(null);

        Assert.Equal("FF", Assert.Single(_dialogs.ClipboardTexts));
    }

    [Fact]
    public void An_empty_row_cannot_be_copied()
    {
        var vm = CreateViewModel();

        Assert.False(Row(vm, 16).CopyCommand.CanExecute(null));
    }

    // =================================================================================
    //  Tezkor tugmalar
    // =================================================================================

    [Theory]
    [InlineData(2)]
    [InlineData(8)]
    [InlineData(16)]
    public void The_quick_buttons_switch_the_input_base(int radix)
    {
        var vm = CreateViewModel();

        vm.SetSourceBaseCommand.Execute(radix);

        Assert.Equal(radix, vm.SourceBase);
    }

    [Fact]
    public void The_quick_buttons_also_accept_a_text_parameter()
    {
        // XAML dan parametr satr bo'lib kelishi mumkin — bu ikkalasini ham qabul qiladi.
        var vm = CreateViewModel();

        vm.SetSourceBaseCommand.Execute("8");

        Assert.Equal(8, vm.SourceBase);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(33)]
    [InlineData("salom")]
    [InlineData(null)]
    public void An_impossible_base_is_ignored_rather_than_thrown(object? parameter)
    {
        var vm = CreateViewModel();

        vm.SetSourceBaseCommand.Execute(parameter);

        Assert.Equal(10, vm.SourceBase);
    }

    // =================================================================================
    //  Yorliqlar
    // =================================================================================

    [Fact]
    public void The_hint_names_the_digits_of_the_chosen_base()
    {
        var vm = CreateViewModel();

        vm.SourceBase = 16;

        Assert.Contains("0–9 va A–F", vm.AllowedDigits);
        Assert.Equal("o'n oltilik", vm.SourceBaseName);
    }

    [Fact]
    public void The_base_list_covers_the_whole_supported_range()
    {
        var vm = CreateViewModel();

        Assert.Equal(31, vm.BaseChoices.Count);
        Assert.Equal("16-lik — o'n oltilik", vm.BaseChoices.Single(choice => choice.Value == 16).Label);
        Assert.Equal(new[] { 2, 8, 10, 16 }, vm.QuickBases);
    }
}
