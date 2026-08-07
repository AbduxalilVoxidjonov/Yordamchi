namespace Yordamchi.Models;

/// <summary>
/// One entry of an output document: "take page <see cref="SourcePageIndex"/> of
/// <see cref="SourceFilePath"/> and rotate it by <see cref="Rotation"/>".
/// <para>
/// A list of these is the single primitive behind every write operation of the app —
/// merge, reorder, delete and rotate are all just different lists of <see cref="PageEdit"/>.
/// </para>
/// </summary>
/// <param name="SourceFilePath">Absolute path of the source PDF.</param>
/// <param name="SourcePageIndex">Zero-based page index inside the source PDF.</param>
/// <param name="Rotation">Extra clockwise rotation to add to the page's intrinsic /Rotate.</param>
public sealed record PageEdit(
    string SourceFilePath,
    int SourcePageIndex,
    PageRotation Rotation = PageRotation.None);
