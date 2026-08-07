using System.Windows.Controls;

namespace Yordamchi.Views;

/// <summary>
/// Universal ishchi oyna. Sahifalarni surib tartiblash <c>Yordamchi.Behaviors.DragDropReorder</c>,
/// fayllarni tashlab qo'shish esa <c>Yordamchi.Behaviors.FileDrop</c> zimmasida — shuning uchun bu
/// sinf bo'sh qoladi va hech qanday holat saqlamaydi.
/// </summary>
public partial class ToolWorkspaceView : UserControl
{
    public ToolWorkspaceView() => InitializeComponent();
}
