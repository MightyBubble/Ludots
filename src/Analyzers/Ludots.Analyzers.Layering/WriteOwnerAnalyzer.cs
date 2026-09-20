using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Ludots.Analyzers.Layering;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WriteOwnerAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LDTS014";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Cross-layer write of an owned component",
        "Type '{0}' is owned by {1}; assembly '{2}' is not allowed to write it",
        "Layering",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not ObjectCreationExpressionSyntax creation)
        {
            return;
        }

        ITypeSymbol? created = context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type;
        ReportIfForbiddenWrite(context, created, creation.GetLocation());
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not AssignmentExpressionSyntax assignment)
        {
            return;
        }

        ISymbol? left = context.SemanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol;
        INamedTypeSymbol? ownerType = left?.ContainingType;
        ReportIfForbiddenWrite(context, ownerType, assignment.GetLocation());
    }

    private static void ReportIfForbiddenWrite(
        SyntaxNodeAnalysisContext context,
        ITypeSymbol? writtenType,
        Location location)
    {
        if (writtenType == null)
        {
            return;
        }

        AttributeData? owner = null;
        foreach (AttributeData attribute in writtenType.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "WriteOwnerAttribute")
            {
                owner = attribute;
                break;
            }
        }

        if (owner == null || owner.ConstructorArguments.Length == 0)
        {
            return;
        }

        string ownerName = owner.ConstructorArguments[0].Value?.ToString() ?? string.Empty;
        bool simulationOwned = ownerName.Contains("Simulation") || ownerName == "1";
        if (!simulationOwned)
        {
            return;
        }

        string assemblyName = context.Compilation.AssemblyName ?? string.Empty;
        if (!IsPresentationAssembly(assemblyName))
        {
            return;
        }

        if (IsBootstrapType(context))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, location, writtenType.Name, "Simulation", assemblyName));
    }

    private static bool IsPresentationAssembly(string assemblyName)
    {
        return assemblyName.IndexOf("Presentation", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsBootstrapType(SyntaxNodeAnalysisContext context)
    {
        ISymbol? containing = context.ContainingSymbol;
        while (containing != null)
        {
            string name = containing.Name;
            if (name.IndexOf("Bootstrap", System.StringComparison.Ordinal) >= 0 ||
                name.IndexOf("Allocator", System.StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            containing = containing.ContainingSymbol;
        }

        return false;
    }
}
