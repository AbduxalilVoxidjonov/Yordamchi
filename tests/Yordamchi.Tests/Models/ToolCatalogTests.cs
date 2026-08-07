using Yordamchi.Models;

namespace Yordamchi.Tests.Models;

/// <summary>
/// <see cref="ToolCatalog"/> — dasturning "yagona haqiqat manbai": bosh sahifadagi
/// kartochkalar ham, ishchi oyna ham undan quriladi. Shuning uchun bu yerdagi sinovlar
/// alohida qiymatlarni emas, <b>katalogning butunligini</b> tekshiradi: yangi vosita
/// qo'shilganda yarim to'ldirilgan yozuv jimgina o'tib ketmasligi kerak.
/// </summary>
public sealed class ToolCatalogTests
{
    [Fact]
    public void Every_declared_tool_id_has_a_catalog_entry()
    {
        var declared = Enum.GetValues<ToolId>();
        var listed = ToolCatalog.All.Select(tool => tool.Id).ToHashSet();

        var missing = declared.Where(id => !listed.Contains(id)).ToArray();

        Assert.True(
            missing.Length == 0,
            $"ToolCatalog da yo'q: {string.Join(", ", missing)}");
    }

    [Fact]
    public void No_tool_is_listed_twice()
    {
        var duplicates = ToolCatalog.All
            .GroupBy(tool => tool.Id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.True(duplicates.Length == 0, $"takrorlangan: {string.Join(", ", duplicates)}");
    }

    [Fact]
    public void Get_returns_the_entry_for_every_id()
    {
        foreach (var id in Enum.GetValues<ToolId>())
            Assert.Equal(id, ToolCatalog.Get(id).Id);
    }

    [Theory]
    [MemberData(nameof(AllTools))]
    public void Every_entry_is_fully_filled_in(ToolDescriptor tool)
    {
        Assert.False(string.IsNullOrWhiteSpace(tool.Title), $"{tool.Id}: sarlavha bo'sh");
        Assert.False(string.IsNullOrWhiteSpace(tool.Description), $"{tool.Id}: tavsif bo'sh");
        Assert.False(string.IsNullOrWhiteSpace(tool.Glyph), $"{tool.Id}: belgi (glyph) bo'sh");
        Assert.False(string.IsNullOrWhiteSpace(tool.CategoryTitle), $"{tool.Id}: kategoriya nomi bo'sh");
    }

    [Theory]
    [MemberData(nameof(AllTools))]
    public void Every_glyph_is_a_single_private_use_character(ToolDescriptor tool)
    {
        // Segoe Fluent Icons belgilari PUA (U+E000..U+F8FF) oralig'ida. Oddiy harf qolib
        // ketsa kartochkada ikona o'rniga o'sha harf chiziladi.
        Assert.Equal(1, tool.Glyph.Length);
        Assert.InRange(tool.Glyph[0], '\uE000', '\uF8FF');
    }

    [Fact]
    public void Tools_that_write_a_folder_are_exactly_the_ones_that_produce_many_files()
    {
        // Bu bayroq ishchi oynada "fayl saqlash" o'rniga "papka tanlash" dialogini ochadi;
        // xato bo'lsa foydalanuvchi natijani umuman saqlay olmaydi.
        var folderWriters = ToolCatalog.All.Where(tool => tool.WritesToFolder).Select(tool => tool.Id).ToHashSet();

        Assert.Equal([ToolId.Split, ToolId.PdfToImage], folderWriters.Order().ToArray());
    }

    [Fact]
    public void Only_page_based_tools_show_thumbnails()
    {
        var withThumbnails = ToolCatalog.All
            .Where(tool => tool.ShowsPageThumbnails)
            .Select(tool => tool.Id)
            .Order()
            .ToArray();

        ToolId[] expected = [ToolId.Merge, ToolId.Split, ToolId.Organize, ToolId.Rotate];

        Assert.Equal(expected.Order().ToArray(), withThumbnails);
    }

    [Fact]
    public void Tools_that_show_thumbnails_take_pdf_input()
    {
        // Eskiz chizish PDF rasterizatsiyasiga tayanadi — rasm yoki Word kirishli vositada
        // bu bayroq yoqilsa, oyna bo'sh eskizlar bilan ochilardi.
        foreach (var tool in ToolCatalog.All.Where(tool => tool.ShowsPageThumbnails))
            Assert.True(tool.Input is ToolInputKind.SinglePdf or ToolInputKind.MultiplePdf, $"{tool.Id}: {tool.Input}");
    }

    [Fact]
    public void Every_category_has_at_least_one_tool()
    {
        // Bo'sh kategoriya bosh sahifada sarlavhasi bor, ichi bo'm-bo'sh blok bo'lib qolardi.
        foreach (var category in Enum.GetValues<ToolCategory>())
            Assert.Contains(ToolCatalog.All, tool => tool.Category == category);
    }

    [Fact]
    public void Titles_are_unique_so_the_dashboard_search_stays_useful()
    {
        var duplicates = ToolCatalog.All
            .GroupBy(tool => tool.Title, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.True(duplicates.Length == 0, $"takrorlangan sarlavha: {string.Join(", ", duplicates)}");
    }

    public static TheoryData<ToolDescriptor> AllTools()
    {
        var data = new TheoryData<ToolDescriptor>();

        foreach (var tool in ToolCatalog.All)
            data.Add(tool);

        return data;
    }
}
