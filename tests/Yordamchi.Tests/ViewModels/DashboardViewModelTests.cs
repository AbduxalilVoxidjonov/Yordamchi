using Yordamchi.Models;
using Yordamchi.Tests.TestSupport;
using Yordamchi.ViewModels;

namespace Yordamchi.Tests.ViewModels;

/// <summary>
/// <see cref="DashboardViewModel"/> sinovlari. Bosh sahifada PDF mantiq yo'q, shuning uchun
/// bu yerda tekshiriladigan narsa <b>ko'rinish qoidalari</b>: katalogdagi hamma vosita
/// ko'rinadimi, qidiruv qanday filtrlaydi va kartochka bosilganda qobiqqa nima uzatiladi.
/// </summary>
public sealed class DashboardViewModelTests
{
    private readonly FakeDialogService _dialogs = new();
    private readonly DashboardViewModel _vm;

    public DashboardViewModelTests() => _vm = new DashboardViewModel(_dialogs);

    // =================================================================================
    //  Boshlang'ich ko'rinish
    // =================================================================================

    [Fact]
    public void The_page_shows_every_tool_from_the_catalog()
    {
        // Kartochka tushib qolsa, vosita foydalanuvchi uchun umuman mavjud bo'lmay qoladi —
        // boshqa hech qayerdan unga yo'l yo'q.
        Assert.Equal(ToolCatalog.All.Count, VisibleTools().Count);
        Assert.Equal(ToolCatalog.All.Count, _vm.ToolCount);
        Assert.False(_vm.HasNoResults);
    }

    [Fact]
    public void The_groups_follow_the_catalog_category_order()
    {
        string[] expected =
        [
            ToolCategory.Pages.ToString(),
            ToolCategory.Convert.ToString(),
            ToolCategory.Optimize.ToString(),
            ToolCategory.Ai.ToString()
        ];

        var actual = _vm.Groups
            .Select(group => ToolCatalog.All.First(tool => tool.CategoryTitle == group.Title).Category.ToString())
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Each_group_holds_exactly_the_tools_of_its_own_category()
    {
        foreach (var group in _vm.Groups)
        {
            var expected = ToolCatalog.All
                .Where(tool => tool.CategoryTitle == group.Title)
                .Select(tool => tool.Id);

            Assert.Equal(expected, group.Tools.Select(card => card.Tool.Id));
            Assert.True(group.HasTools);
        }
    }

    [Fact]
    public void Each_group_gets_an_icon_of_its_own()
    {
        // Bo'limlar bir xil ikonka bilan chiqsa, sarlavhalar bir qarashda ajralmaydi.
        var glyphs = _vm.Groups.Select(group => group.Glyph).ToList();

        Assert.All(glyphs, glyph => Assert.False(string.IsNullOrEmpty(glyph)));
        Assert.Equal(glyphs.Count, glyphs.Distinct().Count());
    }

    // =================================================================================
    //  Qidiruv
    // =================================================================================

    [Fact]
    public void Searching_leaves_only_the_matching_cards()
    {
        _vm.SearchText = "birlashtir";

        Assert.Equal([ToolId.Merge], VisibleTools().Select(card => card.Tool.Id));
        Assert.False(_vm.HasNoResults);
        Assert.True(_vm.HasSearchText);
    }

    [Fact]
    public void A_group_with_no_match_is_hidden_completely()
    {
        // Bo'sh bo'lim sarlavhasi qidiruv natijasini "teshik" qilib ko'rsatadi.
        _vm.SearchText = "birlashtir";

        var pages = GroupOf(ToolCategory.Pages);
        var convert = GroupOf(ToolCategory.Convert);

        Assert.True(pages.HasTools);
        Assert.False(convert.HasTools);
        Assert.Empty(convert.Tools);
    }

    [Theory]
    [InlineData("SUN'IY INTELLEKT")]
    [InlineData("sun'iy intellekt")]
    [InlineData("  sun'iy intellekt  ")]
    public void Search_ignores_letter_case_and_surrounding_spaces(string query)
    {
        // Foydalanuvchi Caps Lock bilan yozgani yoki matnni nusxalab qo'yganida ham
        // natija bir xil bo'lishi kerak.
        _vm.SearchText = query;

        Assert.Equal(
            [ToolId.OcrToWord, ToolId.BackgroundRemover],
            VisibleTools().Select(card => card.Tool.Id));
    }

    [Fact]
    public void Search_also_looks_at_the_description_and_the_category_name()
    {
        // Foydalanuvchi vosita nomini emas, o'zi qidirayotgan natijani yozadi ("skaner").
        _vm.SearchText = "skaner";
        Assert.Contains(VisibleTools(), card => card.Tool.Id == ToolId.OcrToWord);

        _vm.SearchText = "Konvertatsiya";
        Assert.Equal(
            ToolCatalog.All.Where(tool => tool.Category == ToolCategory.Convert).Select(tool => tool.Id),
            VisibleTools().Select(card => card.Tool.Id));
    }

    [Fact]
    public void A_search_with_no_match_raises_the_empty_state()
    {
        _vm.SearchText = "kvant fizikasi";

        Assert.Empty(VisibleTools());
        Assert.True(_vm.HasNoResults);
        Assert.All(_vm.Groups, group => Assert.False(group.HasTools));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_search_text_means_no_filter_at_all(string query)
    {
        _vm.SearchText = query;

        Assert.Equal(ToolCatalog.All.Count, VisibleTools().Count);
        Assert.False(_vm.HasSearchText);
        Assert.False(_vm.HasNoResults);
    }

    [Fact]
    public void Clearing_the_search_brings_every_card_back_in_the_catalog_order()
    {
        // Filtr kartochkalarni ro'yxatdan olib tashlab, keyin qaytarib qo'yadi — shu paytda
        // katalogdagi mantiqiy tartib buzilmasligi kerak.
        _vm.SearchText = "pdf";
        Assert.NotEqual(ToolCatalog.All.Count, VisibleTools().Count);

        _vm.ClearSearchCommand.Execute(null);

        Assert.Equal(string.Empty, _vm.SearchText);
        Assert.False(_vm.HasSearchText);
        Assert.Equal(
            ToolCatalog.All.Select(tool => tool.Id),
            VisibleTools().Select(card => card.Tool.Id));
    }

    [Fact]
    public void Narrowing_the_query_letter_by_letter_keeps_narrowing_the_result()
    {
        // Qidiruv har bir belgida qaytadan qo'llanadi; oldingi filtr natijasi ustiga
        // "yopishib" qolmasligi kerak.
        _vm.SearchText = "p";
        var afterFirstLetter = VisibleTools().Count;

        _vm.SearchText = "pdf b";

        Assert.True(VisibleTools().Count < afterFirstLetter);
        Assert.Contains(VisibleTools(), card => card.Tool.Id == ToolId.Merge);
    }

    // =================================================================================
    //  Vositani ochish
    // =================================================================================

    [Fact]
    public void Opening_a_card_hands_that_exact_tool_to_the_shell()
    {
        // Qobiq (MainViewModel) ishchi oynani faqat shu hodisadagi tavsif asosida ochadi.
        ToolDescriptor? selected = null;
        _vm.ToolSelected += (_, tool) => selected = tool;

        var card = VisibleTools().First(card => card.Tool.Id == ToolId.Watermark);
        card.OpenCommand.Execute(null);

        Assert.Same(card.Tool, selected);
        Assert.Equal(ToolId.Watermark, selected!.Id);
    }

    [Fact]
    public void A_filtered_card_still_opens_the_right_tool()
    {
        // Qidiruvdan keyin kartochkalar ro'yxatda joyini o'zgartiradi — bosilgan kartochka
        // bilan ochiladigan vosita orasidagi bog'lanish shunda ham buzilmasligi kerak.
        ToolDescriptor? selected = null;
        _vm.ToolSelected += (_, tool) => selected = tool;

        _vm.SearchText = "qulf";
        VisibleTools().Single().OpenCommand.Execute(null);

        Assert.Equal(ToolId.Unlock, selected?.Id);
    }

    [Fact]
    public void Nothing_is_opened_while_the_user_only_types()
    {
        var raised = 0;
        _vm.ToolSelected += (_, _) => raised++;

        _vm.SearchText = "pdf";
        _vm.ClearSearchCommand.Execute(null);

        Assert.Equal(0, raised);
    }

    // =================================================================================
    //  Yordamchilar
    // =================================================================================

    private List<ToolCardViewModel> VisibleTools() =>
        _vm.Groups.SelectMany(group => group.Tools).ToList();

    private ToolGroupViewModel GroupOf(ToolCategory category) =>
        _vm.Groups.Single(group => group.Title == ToolCatalog.All.First(tool => tool.Category == category).CategoryTitle);
}
