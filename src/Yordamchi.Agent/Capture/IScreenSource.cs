using Yordamchi.Remoting.Protocol;

namespace Yordamchi.Agent.Capture;

/// <summary>
/// Kadr ish stolining qaysi to'rtburchagini qoplaydi — virtual ish stoli (barcha monitorlar)
/// koordinatalarida.
/// <para>
/// Bu <b>kirishni to'g'ri joyga yuborish uchun</b> kerak: master normallashtirilgan (0..1) o'rin
/// yuboradi, u esa "ko'rilayotgan kadrning shu qismi" degani. GDI butun virtual ish stolini
/// oladi, DXGI esa bitta monitorni — shuning uchun 0.5 qiymati ikki holatda ekranning har xil
/// nuqtasiga tushadi va agent uni kadr to'rtburchagi orqali hisoblashi shart.
/// </para>
/// </summary>
/// <param name="Left">Chap chegara (manfiy bo'lishi mumkin — ikkinchi monitor chapda bo'lsa).</param>
/// <param name="Top">Yuqori chegara.</param>
/// <param name="Width">Kenglik.</param>
/// <param name="Height">Balandlik.</param>
public readonly record struct ScreenRegion(int Left, int Top, int Width, int Height);

/// <summary>Bitta ekran kadri: o'lchami, kodlash turi va rasm baytlari.</summary>
/// <param name="Width">Piksellardagi kenglik.</param>
/// <param name="Height">Piksellardagi balandlik.</param>
/// <param name="Format">Rasm baytlari qanday kodlangani (JPEG yoki xom BGRA).</param>
/// <param name="Image">Rasm baytlari.</param>
public sealed record ScreenFrame(int Width, int Height, ScreenImageFormat Format, byte[] Image);

/// <summary>
/// Ekran kadrini beruvchi manba. Uni interfeys ortiga yashirish maqsadli: bir xil ulanish va
/// uzatish qatlami ustida uchta manba almashadi — <see cref="DxgiScreenSource"/> (tez, GPU),
/// <see cref="GdiScreenSource"/> (hamma joyda ishlaydi) va <see cref="SyntheticScreenSource"/>
/// (apparatsiz sinov uchun). Qaysi biri ishlatilishini <see cref="ScreenSourceFactory"/> hal
/// qiladi.
/// </summary>
public interface IScreenSource : IDisposable
{
    /// <summary>Navbatdagi kadrni oladi.</summary>
    ScreenFrame Capture();

    /// <summary>
    /// Kadr qoplaydigan to'rtburchak (virtual ish stoli koordinatalarida). Ekran o'lchami ish
    /// vaqtida o'zgarishi mumkin (monitor qo'shildi, ruxsat o'zgardi), shuning uchun bu har
    /// murojaatda qaytadan hisoblanadi — bir marta saqlab qo'yilmaydi.
    /// </summary>
    ScreenRegion Bounds { get; }
}
