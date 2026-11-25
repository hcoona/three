// Copyright (c) 2022 Zhang Shuai<zhangshuai.ustc@gmail.com>.
// All rights reserved.
//
// This file is part of OneDotNet.
//
// OneDotNet is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version.
//
// OneDotNet is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS
// FOR A PARTICULAR PURPOSE. See the GNU General Public License for more
// details.
//
// You should have received a copy of the GNU General Public License along with
// OneDotNet. If not, see <https://www.gnu.org/licenses/>.

// Adapted from the MIT-licensed Nanopass Compiler Framework (Copyright (c) 2000-2015 Dipanwita Sarkar, Andrew W. Keep, R. Kent Dybvig, Oscar Waddell). See src/public/lib/Nanopass/THIRD-PARTY-NOTICES.txt.

namespace Hcoona.Nanopass.Tests.Compilers.Scheme;

internal static class SchemeCompiler
{
    internal delegate void StageObserver(string language, bool isTerm, object term, object roundTrip);

    internal static L18.LetrecExpr Compile(SExpr input, StageObserver? observe = null)
    {
        SchemeSymbols symbols = new();
        return Lower(new RenameVarsVerifyLegal().Run(input, symbols), symbols, observe);
    }

    internal static L18.LetrecExpr CompileVerified(LP.Expr input)
    {
        SchemeSymbols symbols = new();
        return Lower(new VerifyScheme().Run(input, symbols), symbols, null);
    }

    private static L18.LetrecExpr Lower(L0.Expr input, SchemeSymbols symbols, StageObserver? observe)
    {
        var l0 = Stage(input, nameof(L0), L0.IsLanguageTerm, L0.Unparse, L0.Parse);
        var l1 = Stage(new RemoveImplicitBegin().Run(l0), nameof(L1), L1.IsLanguageTerm, L1.Unparse, L1.Parse);
        var l2 = Stage(new RemoveUnquotedConstant().Run(l1), nameof(L2), L2.IsLanguageTerm, L2.Unparse, L2.Parse);
        var l3 = Stage(new RemoveOneArmedIf().Run(l2), nameof(L3), L3.IsLanguageTerm, L3.Unparse, L3.Parse);
        var l4 = Stage(new UncoverSettable().Run(l3), nameof(L4), L4.IsLanguageTerm, L4.Unparse, L4.Parse);
        var l5 = Stage(new RemoveImpureLetrec().Run(l4, symbols), nameof(L5), L5.IsLanguageTerm, L5.Unparse, L5.Parse);
        var l6 = Stage(new RemoveSetBang().Run(l5, symbols), nameof(L6), L6.IsLanguageTerm, L6.Unparse, L6.Parse);
        l6 = Stage(new SanitizeBinding().Run(l6), nameof(L6), L6.IsLanguageTerm, L6.Unparse, L6.Parse);
        var l7 = Stage(new RemoveAnonymousLambda().Run(l6, symbols), nameof(L7), L7.IsLanguageTerm, L7.Unparse, L7.Parse);
        var l8 = Stage(new UncoverFree().Run(l7), nameof(L8), L8.IsLanguageTerm, L8.Unparse, L8.Parse);
        var l9 = Stage(new ConvertClosure().Run(l8, symbols), nameof(L9), L9.IsLanguageTerm, L9.Unparse, L9.Parse);
        var l10 = Stage(new LiftLetrec().Run(l9), nameof(L10), L10.IsLanguageTerm, L10.Unparse, L10.Parse);
        var l11 = Stage(new ExplicitClosure().Run(l10), nameof(L11), L11.IsLanguageTerm, L11.Unparse, L11.Parse);
        var l12 = Stage(new NormalizeContext().Run(l11), nameof(L12), L12.IsLanguageTerm, L12.Unparse, L12.Parse);
        var l13 = Stage(new RemoveComplexOpera().Run(l12, symbols), nameof(L13), L13.IsLanguageTerm, L13.Unparse, L13.Parse);
        var l14 = Stage(new RemoveAnonymousCall().Run(l13, symbols), nameof(L14), L14.IsLanguageTerm, L14.Unparse, L14.Parse);
        var l15 = Stage(new IntroduceDummyRp().Run(l14, symbols), nameof(L15), L15.IsLanguageTerm, L15.Unparse, L15.Parse);
        var l16 = Stage(new RemoveNonunaryLet().Run(l15), nameof(L16), L16.IsLanguageTerm, L16.Unparse, L16.Parse);
        var l17 = Stage(new ReturnOfSetBang().Run(l16), nameof(L17), L17.IsLanguageTerm, L17.Unparse, L17.Parse);
        return Stage(new ExplicitLabels().Run(l17), nameof(L18), L18.IsLanguageTerm, L18.Unparse, L18.Parse);

        T Stage<T>(T term, string language, Func<object?, bool> isTerm, Func<T, bool, SExpr> unparse, Func<SExpr, T> parse)
            where T : struct
        {
            observe?.Invoke(language, isTerm(term), term, parse(unparse(term, false)));
            return term;
        }
    }
}
