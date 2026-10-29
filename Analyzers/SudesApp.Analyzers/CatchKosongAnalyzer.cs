using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SudesApp.Analyzers;

/// <summary>
/// Menolak <c>catch</c> kosong yang tidak menjelaskan alasannya. Catch kosong
/// yang sah (<c>catch { /* sisa berkas uji */ }</c>) tetap diterima asalkan
/// komentar alasan ditulis DI DALAM blok catch.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CatchKosongAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SUDES0001";

    private static readonly DiagnosticDescriptor Aturan = new(
        id: DiagnosticId,
        title: "Catch kosong harus menjelaskan alasannya",
        messageFormat: "Catch kosong tanpa komentar alasan; tangani galatnya atau tulis alasan mengapa aman diabaikan",
        category: "Keterbacaan",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Galat yang ditelan tanpa penjelasan menyulitkan diagnosis. " +
                     "Tangani galatnya, catat ke log, atau tulis komentar alasan di dalam blok catch.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Aturan);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analisis, SyntaxKind.CatchClause);
    }

    private static void Analisis(SyntaxNodeAnalysisContext context)
    {
        var klausa = (CatchClauseSyntax)context.Node;
        var blok = klausa.Block;
        if (blok.Statements.Count != 0 || PunyaKomentarAlasan(blok))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Aturan, klausa.CatchKeyword.GetLocation()));
    }

    private static bool PunyaKomentarAlasan(BlockSyntax blok)
    {
        foreach (var trivia in blok.DescendantTrivia(descendIntoTrivia: true))
        {
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia))
            {
                // Buang awalan "//"; komentar kosong (mis. "//") bukan alasan.
                if (!string.IsNullOrWhiteSpace(trivia.ToString().Substring(2)))
                {
                    return true;
                }
            }
            else if (trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            {
                var isi = trivia.ToString();
                if (isi.Length > 4 && !string.IsNullOrWhiteSpace(isi.Substring(2, isi.Length - 4)))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
