using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace WildCat.Scripting
{
    /// <summary>
    /// Instruments a validated script with <see cref="ScriptGuard"/> calls:
    /// <list type="bullet">
    /// <item><c>Check()</c> as the first statement of every for, foreach, while and do body and
    /// in front of every goto (goto, goto case, goto default), so no loop or jump can outlive the
    /// deadline;</item>
    /// <item><c>using (Enter()) { ... }</c> around every method, local function, lambda,
    /// anonymous method, accessor, constructor and operator body, which checks the deadline and
    /// counts call depth (iterator bodies get <c>Check()</c> instead, since their body runs across
    /// many MoveNext calls);</item>
    /// <item><c>Probe() ? value : default(T)</c> around instance field and property initializers,
    /// which run before any constructor body and could otherwise recurse unchecked;</item>
    /// <item><c>Enumerate&lt;T&gt;(sequence)</c> around every argument of a library method or
    /// constructor whose parameter is IEnumerable&lt;T&gt; (or IEnumerable), including the receiver
    /// of an extension method such as <c>source.Sum()</c>, and around the sources of query
    /// expressions, so a library loop over a long lazy sequence (<c>Enumerable.Repeat(1,
    /// int.MaxValue).Sum()</c>) checks the deadline for every item. Arrays, strings, in-memory
    /// collections and the script's own types are left alone. Delegate calls are not wrapped;
    /// the validator rejects library method groups that take a sequence, so a delegate with a
    /// sequence parameter always runs guarded script code. foreach sources need no wrapper:
    /// the loop body checks for every item, and a lazy source built from LINQ has guarded
    /// inputs.</item>
    /// </list>
    /// Expression bodies become block bodies. The semantic model of the original compilation
    /// decides whether an expression body returns a value. Inserted code stays on the lines of
    /// the code it guards, so line numbers of the developer's script do not move.
    /// </summary>
    internal sealed class ScriptGuardRewriter : CSharpSyntaxRewriter
    {
        private const string GuardType = "global::WildCat.Scripting.ScriptGuard";

        private readonly SemanticModel model;

        public ScriptGuardRewriter(SemanticModel model) : base(visitIntoStructuredTrivia: false)
        {
            this.model = model;
        }

        private static StatementSyntax CheckStatement()
        {
            return SyntaxFactory.ParseStatement(GuardType + ".Check();");
        }

        private static ExpressionSyntax EnterCall()
        {
            return SyntaxFactory.ParseExpression(GuardType + ".Enter()");
        }

        private static ExpressionSyntax ProbeCall()
        {
            return SyntaxFactory.ParseExpression(GuardType + ".Probe()");
        }

        // Loops and jumps.

        public override SyntaxNode VisitWhileStatement(WhileStatementSyntax node)
        {
            var visited = (WhileStatementSyntax)base.VisitWhileStatement(node);
            return visited.WithStatement(PrependCheck(visited.Statement));
        }

        public override SyntaxNode VisitDoStatement(DoStatementSyntax node)
        {
            var visited = (DoStatementSyntax)base.VisitDoStatement(node);
            return visited.WithStatement(PrependCheck(visited.Statement));
        }

        public override SyntaxNode VisitForStatement(ForStatementSyntax node)
        {
            var visited = (ForStatementSyntax)base.VisitForStatement(node);
            return visited.WithStatement(PrependCheck(visited.Statement));
        }

        public override SyntaxNode VisitForEachStatement(ForEachStatementSyntax node)
        {
            var visited = (ForEachStatementSyntax)base.VisitForEachStatement(node);
            return visited.WithStatement(PrependCheck(visited.Statement));
        }

        public override SyntaxNode VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
        {
            var visited = (ForEachVariableStatementSyntax)base.VisitForEachVariableStatement(node);
            return visited.WithStatement(PrependCheck(visited.Statement));
        }

        public override SyntaxNode VisitGotoStatement(GotoStatementSyntax node)
        {
            var visited = (GotoStatementSyntax)base.VisitGotoStatement(node);
            return SyntaxFactory.Block(CheckStatement(), visited.WithoutLeadingTrivia().WithoutTrailingTrivia())
                .WithLeadingTrivia(visited.GetLeadingTrivia())
                .WithTrailingTrivia(visited.GetTrailingTrivia());
        }

        private static StatementSyntax PrependCheck(StatementSyntax body)
        {
            if (body is BlockSyntax block)
                return block.WithStatements(block.Statements.Insert(0, CheckStatement()));
            return SyntaxFactory.Block(CheckStatement(), body.WithoutLeadingTrivia().WithoutTrailingTrivia())
                .WithLeadingTrivia(body.GetLeadingTrivia())
                .WithTrailingTrivia(body.GetTrailingTrivia());
        }

        // Bodies.

        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            bool returnsValue = ReturnsValue(model.GetDeclaredSymbol(node));
            bool iterator = IsIterator(node.Body, node.ExpressionBody);
            var visited = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node);
            if (visited.Body == null && visited.ExpressionBody == null)
                return visited;
            var body = GuardBody(visited.Body, visited.ExpressionBody, visited.SemicolonToken, returnsValue, iterator);
            return visited.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body);
        }

        public override SyntaxNode VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            bool returnsValue = ReturnsValue(model.GetDeclaredSymbol(node) as IMethodSymbol);
            bool iterator = IsIterator(node.Body, node.ExpressionBody);
            var visited = (LocalFunctionStatementSyntax)base.VisitLocalFunctionStatement(node);
            if (visited.Body == null && visited.ExpressionBody == null)
                return visited;
            var body = GuardBody(visited.Body, visited.ExpressionBody, visited.SemicolonToken, returnsValue, iterator);
            return visited.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body);
        }

        public override SyntaxNode VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            var visited = (ConstructorDeclarationSyntax)base.VisitConstructorDeclaration(node);
            if (visited.Body == null && visited.ExpressionBody == null)
                return visited;
            var body = GuardBody(visited.Body, visited.ExpressionBody, visited.SemicolonToken, returnsValue: false, iterator: false);
            return visited.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body);
        }

        public override SyntaxNode VisitOperatorDeclaration(OperatorDeclarationSyntax node)
        {
            var visited = (OperatorDeclarationSyntax)base.VisitOperatorDeclaration(node);
            if (visited.Body == null && visited.ExpressionBody == null)
                return visited;
            var body = GuardBody(visited.Body, visited.ExpressionBody, visited.SemicolonToken, returnsValue: true, iterator: false);
            return visited.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body);
        }

        public override SyntaxNode VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
        {
            var visited = (ConversionOperatorDeclarationSyntax)base.VisitConversionOperatorDeclaration(node);
            if (visited.Body == null && visited.ExpressionBody == null)
                return visited;
            var body = GuardBody(visited.Body, visited.ExpressionBody, visited.SemicolonToken, returnsValue: true, iterator: false);
            return visited.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body);
        }

        public override SyntaxNode VisitAccessorDeclaration(AccessorDeclarationSyntax node)
        {
            var symbol = model.GetDeclaredSymbol(node);
            bool iterator = IsIterator(node.Body, node.ExpressionBody);
            var visited = (AccessorDeclarationSyntax)base.VisitAccessorDeclaration(node);
            if (visited.Body == null && visited.ExpressionBody == null)
                return visited;
            var body = GuardBody(visited.Body, visited.ExpressionBody, visited.SemicolonToken, ReturnsValue(symbol), iterator);
            return visited.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body);
        }

        public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            bool iterator = IsIterator(null, node.ExpressionBody);
            bool guardInitializer = node.Initializer != null && IsInstanceMemberOfType(node, node.Modifiers);
            var visited = (PropertyDeclarationSyntax)base.VisitPropertyDeclaration(node);
            if (guardInitializer)
                visited = visited.WithInitializer(visited.Initializer.WithValue(ProbeWrap(visited.Initializer.Value, node.Type)));
            if (visited.ExpressionBody == null)
                return visited;
            var body = GuardBody(null, visited.ExpressionBody, visited.SemicolonToken, returnsValue: true, iterator);
            return visited.WithExpressionBody(null).WithSemicolonToken(default).WithAccessorList(GetterOnly(body));
        }

        public override SyntaxNode VisitIndexerDeclaration(IndexerDeclarationSyntax node)
        {
            bool iterator = IsIterator(null, node.ExpressionBody);
            var visited = (IndexerDeclarationSyntax)base.VisitIndexerDeclaration(node);
            if (visited.ExpressionBody == null)
                return visited;
            var body = GuardBody(null, visited.ExpressionBody, visited.SemicolonToken, returnsValue: true, iterator);
            return visited.WithExpressionBody(null).WithSemicolonToken(default).WithAccessorList(GetterOnly(body));
        }

        public override SyntaxNode VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            bool returnsValue = ReturnsValue(model.GetSymbolInfo(node).Symbol as IMethodSymbol);
            var visited = (AnonymousFunctionExpressionSyntax)base.VisitParenthesizedLambdaExpression(node);
            return GuardAnonymousFunction(visited, returnsValue);
        }

        public override SyntaxNode VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            bool returnsValue = ReturnsValue(model.GetSymbolInfo(node).Symbol as IMethodSymbol);
            var visited = (AnonymousFunctionExpressionSyntax)base.VisitSimpleLambdaExpression(node);
            return GuardAnonymousFunction(visited, returnsValue);
        }

        public override SyntaxNode VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
        {
            bool returnsValue = ReturnsValue(model.GetSymbolInfo(node).Symbol as IMethodSymbol);
            var visited = (AnonymousFunctionExpressionSyntax)base.VisitAnonymousMethodExpression(node);
            return GuardAnonymousFunction(visited, returnsValue);
        }

        // Sequences handed to library code. A library method that pulls items from a sequence
        // (Sum, Distinct, new HashSet<T>(source), string.Join) runs its loop outside the script,
        // where no Check() can reach it, so the sequence itself is wrapped in
        // ScriptGuard.Enumerate and every item it hands out checks the deadline.

        public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node);
            if (model.GetSymbolInfo(node).Symbol is not IMethodSymbol method || !IsLibrary(method)
                || method.MethodKind == MethodKind.DelegateInvoke)
            {
                return visited;
            }
            visited = visited.WithArgumentList(GuardArguments(node, node.ArgumentList, visited.ArgumentList));

            // An extension method called on its receiver (source.Sum()): the receiver is the
            // sequence argument.
            if (method.ReducedFrom != null
                && node.Expression is MemberAccessExpressionSyntax access
                && visited.Expression is MemberAccessExpressionSyntax visitedAccess
                && !StartsWithConditionalBinding(access.Expression))
            {
                var wrapped = GuardSequence(access.Expression, visitedAccess.Expression, ThisParameterType(method));
                if (wrapped != null)
                    visited = visited.WithExpression(visitedAccess.WithExpression(wrapped));
            }
            return visited;
        }

        public override SyntaxNode VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
        {
            var visited = (ConditionalAccessExpressionSyntax)base.VisitConditionalAccessExpression(node);
            // source?.Sum(): the receiver of the first call after ?. is the expression before it.
            if (FirstBoundCall(node.WhenNotNull) is InvocationExpressionSyntax call
                && model.GetSymbolInfo(call).Symbol is IMethodSymbol { ReducedFrom: not null } method
                && IsLibrary(method))
            {
                var wrapped = GuardSequence(node.Expression, visited.Expression, ThisParameterType(method));
                if (wrapped != null)
                    visited = visited.WithExpression(wrapped);
            }
            return visited;
        }

        public override SyntaxNode VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            var visited = (ObjectCreationExpressionSyntax)base.VisitObjectCreationExpression(node);
            if (node.ArgumentList == null || model.GetSymbolInfo(node).Symbol is not IMethodSymbol constructor || !IsLibrary(constructor))
                return visited;
            return visited.WithArgumentList(GuardArguments(node, node.ArgumentList, visited.ArgumentList));
        }

        public override SyntaxNode VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
        {
            var visited = (ImplicitObjectCreationExpressionSyntax)base.VisitImplicitObjectCreationExpression(node);
            if (model.GetSymbolInfo(node).Symbol is not IMethodSymbol constructor || !IsLibrary(constructor))
                return visited;
            return visited.WithArgumentList(GuardArguments(node, node.ArgumentList, visited.ArgumentList));
        }

        public override SyntaxNode VisitConstructorInitializer(ConstructorInitializerSyntax node)
        {
            var visited = (ConstructorInitializerSyntax)base.VisitConstructorInitializer(node);
            if (model.GetSymbolInfo(node).Symbol is not IMethodSymbol constructor || !IsLibrary(constructor))
                return visited;
            return visited.WithArgumentList(GuardArguments(node, node.ArgumentList, visited.ArgumentList));
        }

        public override SyntaxNode VisitPrimaryConstructorBaseType(PrimaryConstructorBaseTypeSyntax node)
        {
            var visited = (PrimaryConstructorBaseTypeSyntax)base.VisitPrimaryConstructorBaseType(node);
            if (model.GetSymbolInfo(node).Symbol is not IMethodSymbol constructor || !IsLibrary(constructor))
                return visited;
            return visited.WithArgumentList(GuardArguments(node, node.ArgumentList, visited.ArgumentList));
        }

        // Query expressions call Where, Select and the others from their clauses, which are not
        // lambdas in the syntax tree, so their sources are guarded instead.

        public override SyntaxNode VisitFromClause(FromClauseSyntax node)
        {
            var visited = (FromClauseSyntax)base.VisitFromClause(node);
            var wrapped = GuardQuerySource(node.Expression, visited.Expression, node.Type != null);
            return wrapped == null ? visited : visited.WithExpression(wrapped);
        }

        public override SyntaxNode VisitJoinClause(JoinClauseSyntax node)
        {
            var visited = (JoinClauseSyntax)base.VisitJoinClause(node);
            var wrapped = GuardQuerySource(node.InExpression, visited.InExpression, node.Type != null);
            return wrapped == null ? visited : visited.WithInExpression(wrapped);
        }

        private bool IsLibrary(IMethodSymbol method)
        {
            return !SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, model.Compilation.Assembly);
        }

        private static ITypeSymbol ThisParameterType(IMethodSymbol reducedExtension)
        {
            var method = reducedExtension.GetConstructedReducedFrom() ?? reducedExtension.ReducedFrom;
            return method.Parameters.Length > 0 ? method.Parameters[0].Type : null;
        }

        /// <summary>
        /// The call right after ?. in <c>a?.B().C()</c>, which is <c>.B()</c>.
        /// </summary>
        private static ExpressionSyntax FirstBoundCall(ExpressionSyntax whenNotNull)
        {
            var expression = whenNotNull;
            while (true)
            {
                switch (expression)
                {
                    case InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax } call:
                        return call;
                    case InvocationExpressionSyntax invocation:
                        expression = invocation.Expression;
                        break;
                    case MemberAccessExpressionSyntax access:
                        expression = access.Expression;
                        break;
                    case ElementAccessExpressionSyntax element:
                        expression = element.Expression;
                        break;
                    case PostfixUnaryExpressionSyntax postfix:
                        expression = postfix.Operand;
                        break;
                    default:
                        return null;
                }
            }
        }

        /// <summary>
        /// True for an expression that continues a ?. chain (<c>.B()</c> in <c>a?.B().C()</c>); it
        /// has no value outside the chain, so it cannot be wrapped.
        /// </summary>
        private static bool StartsWithConditionalBinding(ExpressionSyntax expression)
        {
            while (true)
            {
                switch (expression)
                {
                    case MemberBindingExpressionSyntax:
                    case ElementBindingExpressionSyntax:
                        return true;
                    case InvocationExpressionSyntax invocation:
                        expression = invocation.Expression;
                        break;
                    case MemberAccessExpressionSyntax access:
                        expression = access.Expression;
                        break;
                    case ElementAccessExpressionSyntax element:
                        expression = element.Expression;
                        break;
                    case PostfixUnaryExpressionSyntax postfix:
                        expression = postfix.Operand;
                        break;
                    default:
                        return false;
                }
            }
        }

        /// <summary>
        /// Wraps every argument of a library call whose parameter is a sequence. The semantic model
        /// of the original node maps arguments to parameters (named arguments, optional ones); the
        /// rewritten argument list has the same arguments in the same order.
        /// </summary>
        private ArgumentListSyntax GuardArguments(SyntaxNode original, ArgumentListSyntax originalArguments, ArgumentListSyntax visitedArguments)
        {
            if (originalArguments == null || visitedArguments == null || originalArguments.Arguments.Count == 0)
                return visitedArguments;
            var arguments = model.GetOperation(original) switch
            {
                IInvocationOperation invocation => invocation.Arguments,
                IObjectCreationOperation creation => creation.Arguments,
                _ => default
            };
            if (arguments.IsDefaultOrEmpty)
                return visitedArguments;

            var list = visitedArguments.Arguments;
            foreach (var argument in arguments)
            {
                if (argument.ArgumentKind != ArgumentKind.Explicit || argument.Parameter == null
                    || argument.Syntax is not ArgumentSyntax syntax || !syntax.RefKindKeyword.IsKind(SyntaxKind.None))
                {
                    continue;
                }
                int index = originalArguments.Arguments.IndexOf(syntax);
                if (index < 0)
                    continue;
                var wrapped = GuardSequence(syntax.Expression, list[index].Expression, argument.Parameter.Type);
                if (wrapped != null)
                    list = list.Replace(list[index], list[index].WithExpression(wrapped));
            }
            return visitedArguments.WithArguments(list);
        }

        /// <summary>
        /// <c>ScriptGuard.Enumerate&lt;T&gt;(expression)</c> when <paramref name="parameterType"/> is
        /// IEnumerable&lt;T&gt;, <c>ScriptGuard.EnumerateUntyped(expression)</c> when it is the
        /// non-generic IEnumerable, or null when the expression needs no guard. The type argument
        /// is the parameter's, so overload resolution and type inference of the library call do
        /// not change.
        /// </summary>
        private ExpressionSyntax GuardSequence(ExpressionSyntax original, ExpressionSyntax visited, ITypeSymbol parameterType)
        {
            if (parameterType == null)
                return null;
            var argumentType = model.GetTypeInfo(original).Type;
            if (argumentType == null || argumentType.TypeKind == TypeKind.Error || NeedsNoGuard(argumentType))
                return null;
            if (IsGenericEnumerable(parameterType, out var element))
                return WrapSequence(visited, element, original.SpanStart);
            if (parameterType.SpecialType == SpecialType.System_Collections_IEnumerable)
                return WrapSequence(visited, null, original.SpanStart);
            return null;
        }

        private ExpressionSyntax GuardQuerySource(ExpressionSyntax original, ExpressionSyntax visited, bool castsItems)
        {
            var type = model.GetTypeInfo(original).Type;
            if (type == null || type.TypeKind == TypeKind.Error || NeedsNoGuard(type))
                return null;
            var sequences = (IsGenericEnumerable(type, out var own) ? new[] { own } : type.AllInterfaces
                    .Where(i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
                    .Select(i => i.TypeArguments[0]))
                .Distinct<ITypeSymbol>(SymbolEqualityComparer.Default)
                .ToList();
            if (sequences.Count == 1)
                return WrapSequence(visited, sequences[0], original.SpanStart);
            if (sequences.Count == 0 && castsItems
                && (type.SpecialType == SpecialType.System_Collections_IEnumerable
                    || type.AllInterfaces.Any(i => i.SpecialType == SpecialType.System_Collections_IEnumerable)))
            {
                return WrapSequence(visited, null, original.SpanStart);
            }
            return null;
        }

        private ExpressionSyntax WrapSequence(ExpressionSyntax visited, ITypeSymbol element, int position)
        {
            string callee = element == null
                ? GuardType + ".EnumerateUntyped"
                : $"{GuardType}.Enumerate<{element.ToMinimalDisplayString(model, position)}>";
            return SyntaxFactory.InvocationExpression(
                    SyntaxFactory.ParseExpression(callee),
                    SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(visited.WithoutTrivia()))))
                .WithLeadingTrivia(visited.GetLeadingTrivia())
                .WithTrailingTrivia(visited.GetTrailingTrivia());
        }

        private static bool IsGenericEnumerable(ITypeSymbol type, out ITypeSymbol element)
        {
            if (type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            {
                element = named.TypeArguments[0];
                return true;
            }
            element = null;
            return false;
        }

        // In-memory collections: iterating one costs at most its size, which the script already
        // paid for under the guards when it filled it. Arrays and strings count too.
        private static readonly HashSet<string> MaterializedCollections = new HashSet<string>(StringComparer.Ordinal)
        {
            "System.Collections.Generic.List`1", "System.Collections.Generic.HashSet`1",
            "System.Collections.Generic.Dictionary`2", "System.Collections.Generic.Dictionary`2+KeyCollection",
            "System.Collections.Generic.Dictionary`2+ValueCollection", "System.Collections.Generic.Queue`1",
            "System.Collections.Generic.Stack`1"
        };

        /// <summary>
        /// Arrays, strings and in-memory collections need no guard, and neither do the script's
        /// own types, whose enumerators are script code with guards of their own.
        /// </summary>
        private bool NeedsNoGuard(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol || type.SpecialType == SpecialType.System_String)
                return true;
            if (type is not INamedTypeSymbol named)
                return false;
            if (SymbolEqualityComparer.Default.Equals(named.ContainingAssembly, model.Compilation.Assembly))
                return true;
            return MaterializedCollections.Contains(MetadataName(named.OriginalDefinition));
        }

        private static string MetadataName(INamedTypeSymbol type)
        {
            string name = type.MetadataName;
            for (var outer = type.ContainingType; outer != null; outer = outer.ContainingType)
                name = outer.MetadataName + "+" + name;
            string ns = type.ContainingNamespace?.ToDisplayString();
            return string.IsNullOrEmpty(ns) || type.ContainingNamespace.IsGlobalNamespace ? name : ns + "." + name;
        }

        public override SyntaxNode VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            var visited = (VariableDeclaratorSyntax)base.VisitVariableDeclarator(node);
            if (visited.Initializer != null
                && node.Parent is VariableDeclarationSyntax declaration
                && declaration.Parent is FieldDeclarationSyntax field
                && IsInstanceMemberOfType(field, field.Modifiers))
            {
                return visited.WithInitializer(visited.Initializer.WithValue(ProbeWrap(visited.Initializer.Value, declaration.Type)));
            }
            return visited;
        }

        private SyntaxNode GuardAnonymousFunction(AnonymousFunctionExpressionSyntax function, bool returnsValue)
        {
            if (function.Block != null)
                return function.WithBlock(GuardBody(function.Block, null, default, returnsValue, iterator: false));
            var arrow = SyntaxFactory.ArrowExpressionClause(function.ExpressionBody);
            return function.WithExpressionBody(null)
                .WithBlock(GuardBody(null, arrow, default, returnsValue, iterator: false));
        }

        /// <summary>
        /// Instance fields and properties declared inside a script type. Top-level script fields
        /// run in the script body itself, and static ones only once per type.
        /// </summary>
        private static bool IsInstanceMemberOfType(MemberDeclarationSyntax member, SyntaxTokenList modifiers)
        {
            return member.Parent is TypeDeclarationSyntax
                && !modifiers.Any(SyntaxKind.StaticKeyword)
                && !modifiers.Any(SyntaxKind.ConstKeyword);
        }

        private static ExpressionSyntax ProbeWrap(ExpressionSyntax value, TypeSyntax type)
        {
            ExpressionSyntax expression = value;
            if (value is InitializerExpressionSyntax initializer && type is ArrayTypeSyntax arrayType)
            {
                expression = SyntaxFactory.ArrayCreationExpression(
                    SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space),
                    arrayType.WithoutTrivia(),
                    initializer);
            }
            return SyntaxFactory.ConditionalExpression(
                ProbeCall(),
                SyntaxFactory.Token(SyntaxKind.QuestionToken).WithLeadingTrivia(SyntaxFactory.Space).WithTrailingTrivia(SyntaxFactory.Space),
                SyntaxFactory.ParenthesizedExpression(expression.WithoutTrivia()),
                SyntaxFactory.Token(SyntaxKind.ColonToken).WithLeadingTrivia(SyntaxFactory.Space).WithTrailingTrivia(SyntaxFactory.Space),
                SyntaxFactory.DefaultExpression(type.WithoutTrivia()))
                .WithLeadingTrivia(value.GetLeadingTrivia())
                .WithTrailingTrivia(value.GetTrailingTrivia());
        }

        private static AccessorListSyntax GetterOnly(BlockSyntax body)
        {
            return SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(
                SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration, body)
                    .WithKeyword(SyntaxFactory.Token(SyntaxKind.GetKeyword).WithTrailingTrivia(SyntaxFactory.Space))));
        }

        /// <summary>
        /// Builds <c>{ using (ScriptGuard.Enter()) { statements } }</c>, or
        /// <c>{ ScriptGuard.Check(); statements }</c> for an iterator. The braces of the original
        /// block, or the arrow and semicolon of an expression body, keep their trivia.
        /// </summary>
        private static BlockSyntax GuardBody(BlockSyntax block, ArrowExpressionClauseSyntax arrow, SyntaxToken semicolon,
            bool returnsValue, bool iterator)
        {
            SyntaxToken open, close;
            SyntaxList<StatementSyntax> statements;
            if (block != null)
            {
                open = block.OpenBraceToken;
                close = block.CloseBraceToken;
                statements = block.Statements;
            }
            else
            {
                open = SyntaxFactory.Token(SyntaxKind.OpenBraceToken)
                    .WithLeadingTrivia(arrow.ArrowToken.LeadingTrivia)
                    .WithTrailingTrivia(arrow.ArrowToken.TrailingTrivia);
                close = SyntaxFactory.Token(SyntaxKind.CloseBraceToken)
                    .WithLeadingTrivia(semicolon.LeadingTrivia)
                    .WithTrailingTrivia(semicolon.TrailingTrivia);
                statements = SyntaxFactory.SingletonList(ToStatement(arrow.Expression, returnsValue));
            }

            if (iterator)
                return SyntaxFactory.Block(open, statements.Insert(0, CheckStatement()), close);

            var guarded = SyntaxFactory.UsingStatement(
                SyntaxFactory.Token(SyntaxKind.UsingKeyword).WithTrailingTrivia(SyntaxFactory.Space),
                SyntaxFactory.Token(SyntaxKind.OpenParenToken),
                null,
                EnterCall(),
                SyntaxFactory.Token(SyntaxKind.CloseParenToken),
                SyntaxFactory.Block(statements));
            return SyntaxFactory.Block(open, SyntaxFactory.SingletonList<StatementSyntax>(guarded), close);
        }

        private static StatementSyntax ToStatement(ExpressionSyntax expression, bool returnsValue)
        {
            var semicolon = SyntaxFactory.Token(SyntaxKind.SemicolonToken);
            if (expression is ThrowExpressionSyntax throwExpression)
            {
                return SyntaxFactory.ThrowStatement(
                    SyntaxFactory.Token(SyntaxKind.ThrowKeyword).WithTrailingTrivia(SyntaxFactory.Space),
                    throwExpression.Expression, semicolon);
            }
            if (returnsValue)
            {
                return SyntaxFactory.ReturnStatement(
                    SyntaxFactory.Token(SyntaxKind.ReturnKeyword).WithTrailingTrivia(SyntaxFactory.Space),
                    expression, semicolon);
            }
            return SyntaxFactory.ExpressionStatement(expression, semicolon);
        }

        /// <summary>
        /// Whether a body's expression form must become <c>return expr;</c>: false for void and
        /// for async methods returning a non-generic Task. A missing symbol fails the compile
        /// rather than leaving a body unguarded.
        /// </summary>
        private static bool ReturnsValue(IMethodSymbol method)
        {
            if (method == null)
                throw new InvalidOperationException("The sandbox could not bind a script body.");
            if (method.ReturnsVoid)
                return false;
            if (method.IsAsync)
                return method.ReturnType is INamedTypeSymbol named && named.IsGenericType;
            return true;
        }

        private static bool IsIterator(BlockSyntax body, ArrowExpressionClauseSyntax expressionBody)
        {
            SyntaxNode root = (SyntaxNode)body ?? expressionBody;
            if (root == null)
                return false;
            return root.DescendantNodes(n => n is not AnonymousFunctionExpressionSyntax && n is not LocalFunctionStatementSyntax)
                .OfType<YieldStatementSyntax>()
                .Any();
        }
    }
}
