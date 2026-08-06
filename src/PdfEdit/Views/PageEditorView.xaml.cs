using System.Windows.Controls;

namespace PdfEdit.Views;

/// <summary>
/// Thumbnail grid for reordering, rotating and deleting pages.
/// Reordering is handled entirely by <c>PdfEdit.Behaviors.DragDropReorder</c>, so this class
/// stays empty — no drag state, no index maths, nothing that belongs in the view model.
/// </summary>
public partial class PageEditorView : UserControl
{
    public PageEditorView() => InitializeComponent();
}
