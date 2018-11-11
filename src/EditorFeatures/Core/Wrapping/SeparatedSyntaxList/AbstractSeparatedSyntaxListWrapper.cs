﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.LanguageServices;
using Microsoft.CodeAnalysis.Shared.Extensions;

namespace Microsoft.CodeAnalysis.Editor.Wrapping.SeparatedSyntaxList
{
    /// <summary>
    /// Base type for all wrappers that involve wrapping a comma-separated list of items.
    /// </summary>
    internal abstract partial class AbstractSeparatedSyntaxListWrapper<
        TListSyntax,
        TListItemSyntax>
        : AbstractWrapper
        where TListSyntax : SyntaxNode
        where TListItemSyntax : SyntaxNode
    {
        protected abstract string ListName { get; }
        protected abstract string ItemNamePlural { get; }
        protected abstract string ItemNameSingular { get; }

        protected abstract IBlankLineIndentationService GetIndentationService();

        protected abstract TListSyntax GetApplicableList(SyntaxNode node);
        protected abstract SeparatedSyntaxList<TListItemSyntax> GetListItems(TListSyntax listSyntax);
        protected abstract bool PositionIsApplicable(
            SyntaxNode root, int position, SyntaxNode declaration, TListSyntax listSyntax);

        public override async Task<ImmutableArray<CodeAction>> ComputeRefactoringsAsync(
            Document document, int position, SyntaxNode declaration, CancellationToken cancellationToken)
        {
            var listSyntax = GetApplicableList(declaration);
            if (listSyntax == null)
            {
                return default;
            }

            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (!PositionIsApplicable(root, position, declaration, listSyntax))
            {
                return default;
            }

            var listItems = GetListItems(listSyntax);
            if (listItems.Count <= 1)
            {
                // nothing to do with 0-1 items.  Simple enough for users to just edit
                // themselves, and this prevents constant clutter with formatting that isn't
                // really that useful.
                return default;
            }

            var containsUnformattableContent = await ContainsUnformattableContentAsync(
                document, listItems.GetWithSeparators(), cancellationToken).ConfigureAwait(false);

            if (containsUnformattableContent)
            {
                return default;
            }

            // If there are comments between any nodes/tokens in the list then don't offer the
            // refactoring.  We'll likely not be able to properly keep the comments in the right
            // place as we move things around.
            var openToken = listSyntax.GetFirstToken();
            var closeToken = listSyntax.GetLastToken();
            var syntaxFacts = document.GetLanguageService<ISyntaxFactsService>();

            if (ContainsNonWhitespaceTrivia(syntaxFacts, openToken.TrailingTrivia) ||
                ContainsNonWhitespaceTrivia(syntaxFacts, closeToken.LeadingTrivia))
            {
                return default;
            }

            var options = await document.GetOptionsAsync(cancellationToken).ConfigureAwait(false);
            var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var computer = new CodeActionComputer(this, document, sourceText, options, listSyntax, listItems);
            var codeActions = await computer.GetTopLevelCodeActionsAsync(cancellationToken).ConfigureAwait(false);
            return codeActions;
        }
    }
}
