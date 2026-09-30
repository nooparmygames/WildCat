using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
    /// which run before any constructor body and could otherwise recurse unchecked.</item>
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
