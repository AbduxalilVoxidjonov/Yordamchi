using Yordamchi.Models;

namespace Yordamchi.Tests.Models;

/// <summary>Sahifa burilishi — normalizatsiyasi oson buziladigan modul arifmetikasi.</summary>
public sealed class PageRotationTests
{
    [Theory]
    [InlineData(PageRotation.None, 90, PageRotation.Rotate90)]
    [InlineData(PageRotation.Rotate270, 90, PageRotation.None)]
    [InlineData(PageRotation.None, -90, PageRotation.Rotate270)]
    [InlineData(PageRotation.Rotate90, 180, PageRotation.Rotate270)]
    [InlineData(PageRotation.Rotate180, 360, PageRotation.Rotate180)]
    public void Add_normalises_into_the_zero_to_270_range(PageRotation start, int degrees, PageRotation expected) =>
        Assert.Equal(expected, start.Add(degrees));

    [Fact]
    public void Add_handles_several_full_turns_in_both_directions()
    {
        Assert.Equal(PageRotation.Rotate90, PageRotation.None.Add(90 + 360 * 3));
        Assert.Equal(PageRotation.Rotate270, PageRotation.None.Add(-90 - 360 * 3));
    }

    [Fact]
    public void Four_clockwise_turns_return_to_the_start()
    {
        var rotation = PageRotation.Rotate90;

        for (var i = 0; i < 4; i++)
            rotation = rotation.RotateClockwise();

        Assert.Equal(PageRotation.Rotate90, rotation);
    }

    [Fact]
    public void Clockwise_and_counter_clockwise_cancel_each_other()
    {
        foreach (var start in Enum.GetValues<PageRotation>())
            Assert.Equal(start, start.RotateClockwise().RotateCounterClockwise());
    }

    [Theory]
    [InlineData(PageRotation.None, false)]
    [InlineData(PageRotation.Rotate90, true)]
    [InlineData(PageRotation.Rotate180, false)]
    [InlineData(PageRotation.Rotate270, true)]
    public void IsQuarterTurn_is_true_exactly_when_width_and_height_swap(PageRotation rotation, bool expected) =>
        Assert.Equal(expected, rotation.IsQuarterTurn());
}

/// <summary>Arxiv modellaridagi hisoblanadigan xossalar.</summary>
public sealed class ArchiveModelTests
{
    [Theory]
    [InlineData("hujjatlar/shartnoma.pdf", "shartnoma.pdf")]
    [InlineData("shartnoma.pdf", "shartnoma.pdf")]
    [InlineData("a/b/c/fayl.txt", "fayl.txt")]
    [InlineData("papka/", "papka")]
    [InlineData("a\\b\\windows-uslubi.txt", "windows-uslubi.txt")]
    public void Entry_name_is_the_last_segment_of_the_path(string path, string expected)
    {
        var entry = new ArchiveEntryInfo(path, 0, 0, null, false, false);

        Assert.Equal(expected, entry.Name);
    }

    [Fact]
    public void Saved_percent_shows_how_much_compression_helped()
    {
        var entry = new ArchiveEntryInfo("a.txt", Size: 1000, CompressedSize: 250, null, false, false);

        Assert.Equal(75, entry.SavedPercent);
    }

    [Theory]
    [InlineData(0, 100)]     // manba hajmi noma'lum
    [InlineData(100, 0)]     // siqilgan hajm noma'lum
    [InlineData(100, 150)]   // siqilgani kattaroq (allaqachon siqilgan fayl)
    public void Saved_percent_is_null_when_it_would_be_meaningless(long size, long compressed)
    {
        var entry = new ArchiveEntryInfo("a.txt", size, compressed, null, false, false);

        Assert.Null(entry.SavedPercent);
    }

    [Fact]
    public void Archive_file_count_ignores_folder_entries()
    {
        var info = new ArchiveInfo(
            ArchiveFormat.Zip,
            [
                new ArchiveEntryInfo("papka/", 0, 0, null, IsDirectory: true, false),
                new ArchiveEntryInfo("papka/a.txt", 10, 5, null, false, false),
                new ArchiveEntryInfo("papka/b.txt", 10, 5, null, false, false)
            ],
            20,
            false);

        Assert.Equal(2, info.FileCount);
    }

    [Theory]
    [InlineData(ArchiveCompressionLevel.Store, 0)]
    [InlineData(ArchiveCompressionLevel.Fast, 3)]
    [InlineData(ArchiveCompressionLevel.Normal, 6)]
    [InlineData(ArchiveCompressionLevel.Maximum, 9)]
    public void Compression_level_maps_onto_the_deflate_scale(ArchiveCompressionLevel level, int expected)
    {
        var options = new CreateArchiveOptions { Level = level };

        Assert.Equal(expected, options.DeflateLevel);
    }

    [Fact]
    public void Deflate_level_always_stays_inside_the_valid_range()
    {
        foreach (var level in Enum.GetValues<ArchiveCompressionLevel>())
            Assert.InRange(new CreateArchiveOptions { Level = level }.DeflateLevel, 0, 9);
    }

    [Fact]
    public void Default_options_are_the_safe_everyday_choice()
    {
        var options = CreateArchiveOptions.Default;

        Assert.Equal(ArchiveCompressionLevel.Normal, options.Level);
        Assert.True(options.KeepFolderStructure);
        Assert.Null(options.Password);
        Assert.Equal(ZipEncryption.Aes256, options.Encryption);
    }
}

/// <summary>Bajarilgan topshiriq natijasi.</summary>
public sealed class ToolRunResultTests
{
    [Fact]
    public void Primary_output_is_the_first_file()
    {
        var result = ToolRunResult.Ok("tayyor", "bir.pdf", "ikki.pdf");

        Assert.Equal("bir.pdf", result.PrimaryOutput);
        Assert.True(result.Success);
    }

    [Fact]
    public void Primary_output_is_null_when_nothing_was_written()
    {
        // Natija faylsiz amal ham bo'lishi mumkin — UI "Papkada ko'rsatish" tugmasini
        // shu qiymatga qarab yashiradi, shuning uchun u istisno tashlamasligi kerak.
        var result = ToolRunResult.Ok("tayyor");

        Assert.Null(result.PrimaryOutput);
    }
}
