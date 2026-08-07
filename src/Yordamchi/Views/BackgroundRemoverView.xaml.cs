using System.Windows.Controls;

namespace Yordamchi.Views;

/// <summary>
/// "Orqa fonni olib tashlash" ishchi oynasi. Butun mantiq view model va biriktirilgan
/// xatti-harakatlarda (behaviors) — bu yerda faqat XAML ni yuklash qoladi.
/// </summary>
public partial class BackgroundRemoverView : UserControl
{
    public BackgroundRemoverView() => InitializeComponent();
}
