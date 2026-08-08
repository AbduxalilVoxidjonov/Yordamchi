namespace Yordamchi.Agent.Capture;

/// <summary>Bitta ekran kadri: o'lchami va rasm baytlari.</summary>
/// <param name="Width">Piksellardagi kenglik.</param>
/// <param name="Height">Piksellardagi balandlik.</param>
/// <param name="Image">Rasm baytlari (hozircha xom BGRA; keyin siqilgan format).</param>
public sealed record ScreenFrame(int Width, int Height, byte[] Image);

/// <summary>
/// Ekran kadrini beruvchi manba. Uni interfeys ortiga yashirish maqsadli: hozir
/// <see cref="SyntheticScreenSource"/> (apparatsiz, sinov uchun) ishlatiladi, keyingi
/// bosqichda esa DXGI Desktop Duplication'ga asoslangan haqiqiy manba <b>o'rniga qo'yiladi</b>
/// — ulanish, protokol va uzatish qatlamiga tegmasdan.
/// </summary>
public interface IScreenSource : IDisposable
{
    /// <summary>Navbatdagi kadrni oladi.</summary>
    ScreenFrame Capture();
}
