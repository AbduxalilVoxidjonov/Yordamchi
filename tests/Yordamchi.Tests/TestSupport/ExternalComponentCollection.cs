namespace Yordamchi.Tests.TestSupport;

/// <summary>
/// Tashqi komponentlarni (OCR til fayllari, AI modeli) topish mantiqi <b>global holatga</b>
/// tayanadi: <c>TESSDATA_PREFIX</c> muhit o'zgaruvchisi va dastur papkasidagi <c>Models</c>
/// jildi. Bunday sinovlar parallel ishlasa bir-birining muhitini buzadi, shuning uchun ular
/// shu to'plamga qo'shiladi va xUnit ularni ketma-ket bajaradi.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ExternalComponentCollection
{
    public const string Name = "Tashqi komponentlar";
}
