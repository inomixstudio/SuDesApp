using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SudesPdf;

/// <summary>
/// Paginator WPF yang malas: tiap halaman dirasterkan baru ketika printer
/// memintanya, sehingga dokumen panjang tidak pernah ditahan utuh di memori.
/// </summary>
internal sealed class PdfPrintPaginator : DocumentPaginator, IDocumentPaginatorSource
{
    private readonly Document _document;

    public PdfPrintPaginator(Document document)
    {
        _document = document;
        PageSize = new System.Windows.Size(ToDip(document.PageSizes[0].Width), ToDip(document.PageSizes[0].Height));
    }

    public override bool IsPageCountValid => true;

    public override int PageCount => _document.PageCount;

    public override System.Windows.Size PageSize { get; set; }

    public override IDocumentPaginatorSource Source => this;

    public DocumentPaginator DocumentPaginator => this;

    public override DocumentPage GetPage(int pageNumber)
    {
        if ((uint)pageNumber >= (uint)PageCount) return DocumentPage.Missing;
        var size = _document.PageSizes[pageNumber];
        double width = ToDip(size.Width);
        double height = ToDip(size.Height);
        var page = new FixedPage { Width = width, Height = height, Background = System.Windows.Media.Brushes.White };
        var content = new System.Windows.Controls.Image
        {
            Source = Document.ToBitmap(_document.Render(pageNumber, 1.0)),
            Width = width,
            Height = height,
            Stretch = Stretch.Fill
        };
        FixedPage.SetLeft(content, 0);
        FixedPage.SetTop(content, 0);
        page.Children.Add(content);
        page.Measure(new System.Windows.Size(width, height));
        page.Arrange(new Rect(0, 0, width, height));
        page.UpdateLayout();
        return new DocumentPage(page, new System.Windows.Size(width, height), new Rect(0, 0, width, height), new Rect(0, 0, width, height));
    }

    private static double ToDip(double points) => points * 96.0 / 72.0;
}
