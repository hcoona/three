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

// Adapted from the MIT-licensed Nanopass Compiler Framework (Copyright (c) 2000-2015
// Dipanwita Sarkar, Andrew W. Keep, R. Kent Dybvig, Oscar Waddell).
// See src/public/lib/Nanopass/THIRD-PARTY-NOTICES.txt.

namespace Hcoona.Nanopass.Tests.Compilers.Scheme;

internal static class SchemeSourceEvaluator
{
    internal static object? EvalSource(SExpr expression) => Evaluate(expression, new(null));

    private static object? Evaluate(SExpr expression, Environment environment)
    {
        while (true)
        {
            if (expression is SExpr.Sym(var name))
            {
                return environment.Lookup(name).Value;
            }

            if (expression is not SExpr.Pair)
            {
                return SchemeValues.FromDatum(expression);
            }

            var form = SchemeHelpers.RequireList(expression);
            if (form[0] is SExpr.Sym(var head) && !environment.TryLookup(head, out _))
            {
                switch (head.Name)
                {
                    case "quote" when form.Count == 2:
                        return SchemeValues.FromDatum(form[1]);
                    case "set!" when form.Count == 3:
                        environment.Lookup(SchemeHelpers.RequireSymbol(form[1])).Value =
                            Evaluate(form[2], environment);
                        return SchemeVoid.Instance;
                    case "if" when form.Count is 3 or 4:
                        if (SchemeValues.IsTrue(Evaluate(form[1], environment)))
                        {
                            expression = form[2];
                        }
                        else if (form.Count == 4)
                        {
                            expression = form[3];
                        }
                        else
                        {
                            return SchemeVoid.Instance;
                        }

                        continue;
                    case "begin" when form.Count >= 2:
                        expression = EvaluatePrefix(form[1..], environment);
                        continue;
                    case "lambda" when form.Count >= 3:
                        return new Procedure(
                            SchemeHelpers.RequireList(form[1]).Select(SchemeHelpers.RequireSymbol),
                            form[2..],
                            environment);
                    case "let" or "letrec" when form.Count >= 3:
                        environment = Bind(form[1], environment, recursive: head.Name == "letrec");
                        expression = EvaluatePrefix(form[2..], environment);
                        continue;
                    default:
                        if (SchemeHelpers.AcceptsUserArity(head, form.Count - 1))
                        {
                            return SchemeValues.ApplyPrimitive(
                                head,
                                form.Skip(1)
                                    .Select(value => Evaluate(value, environment)).ToArray());
                        }

                        break;
                }
            }

            if (Evaluate(form[0], environment) is not Procedure procedure)
            {
                throw new NanopassException($"Not a Scheme procedure: {form[0]}.");
            }

            var arguments = form.Skip(1).Select(value => Evaluate(value, environment)).ToArray();
            if (arguments.Length != procedure.Parameters.Count)
            {
                throw new NanopassException($"Procedure arity mismatch: {expression}.");
            }

            environment = new(procedure.Captured);
            for (int i = 0; i < arguments.Length; i++)
            {
                environment.Define(procedure.Parameters[i], arguments[i]);
            }

            expression = EvaluatePrefix(procedure.Body, environment);
        }
    }

    private static SExpr EvaluatePrefix(Seq<SExpr> body, Environment environment)
    {
        for (int i = 0; i < body.Count - 1; i++)
        {
            _ = Evaluate(body[i], environment);
        }

        return body[^1];
    }

    private static Environment Bind(SExpr bindings, Environment parent, bool recursive)
    {
        Environment child = new(parent);
        var pairs = SchemeHelpers.RequireList(bindings).Select(SchemeHelpers.RequireList).ToArray();
        if (recursive)
        {
            foreach (var binding in pairs)
            {
                child.Define(SchemeHelpers.RequireSymbol(binding[0]), Uninitialized.Instance);
            }
        }

        foreach (var binding in pairs)
        {
            Symbol name = SchemeHelpers.RequireSymbol(binding[0]);
            object? value = Evaluate(binding[1], recursive ? child : parent);
            if (recursive)
            {
                child.Lookup(name).Value = value;
            }
            else
            {
                child.Define(name, value);
            }
        }

        return child;
    }

    private sealed record Procedure(
        Seq<Symbol> Parameters, Seq<SExpr> Body, Environment Captured) : ISchemeProcedure;

    private sealed class Environment(Environment? parent)
    {
        private readonly Dictionary<Symbol, Cell> bindings = [];

        private Environment? Parent { get; } = parent;

        internal void Define(Symbol name, object? value) => this.bindings.Add(name, new(value));

        internal bool TryLookup(Symbol name, out Cell? cell)
        {
            for (Environment? current = this; current is not null; current = current.Parent)
            {
                if (current.bindings.TryGetValue(name, out cell))
                {
                    return true;
                }
            }

            cell = null;
            return false;
        }

        internal Cell Lookup(Symbol name) =>
            this.TryLookup(name, out var cell) ? cell! :
                throw new NanopassException($"Unbound Scheme variable: {name}.");
    }

    private sealed class Cell(object? initial)
    {
        private object? value = initial;

        internal object? Value
        {
            get => this.value is Uninitialized
                ? throw new NanopassException("Read of an uninitialized letrec binding.")
                : this.value;
            set => this.value = value;
        }
    }

    private sealed class Uninitialized
    {
        internal static Uninitialized Instance { get; } = new();
    }
}

internal static class SchemeL18Evaluator
{
    internal static object? EvalL18(L18.LetrecExpr ir) => new Machine((L18.Letrec)ir.Value!).Run();

    private sealed class Machine(L18.Letrec program)
    {
        private readonly Dictionary<Symbol, L18.LambdaExpr> labels =
            program.X1List.Zip(program.LexprList).ToDictionary();
        private readonly Stack<Continuation> continuations = new();
        private readonly ReturnAddress halt = new();
        private Action? next;
        private object? answer;

        internal object? Run()
        {
            var run = (L18.Run)program.Rnexpr.Value!;
            var declaration = (L18.Declare)run.Dec.Value!;
            var frame = new Frame(declaration.XList);
            frame.Define(run.X, this.halt);
            this.next = () => this.Tail(declaration.Tl, frame);
            while (this.next is { } step)
            {
                this.next = null;
                step();
            }

            return this.answer;
        }

        private static object? Triv(
            L18.Triv expression, Frame frame, Symbol returnLabel = default,
            ReturnAddress? returnAddress = null) =>
            expression.Value switch
            {
                L18.Var(var name) => frame.Get(name),
                L18.QuotedConst(var datum) => SchemeValues.FromDatum(datum),
                L18.Label(var name) when returnAddress is not null && name == returnLabel =>
                    returnAddress,
                L18.Label(var name) => name,
                _ => throw Invalid(expression),
            };

        private static object? Primitive(Symbol name, Seq<L18.Triv> operands, Frame frame) =>
            SchemeValues.ApplyPrimitive(
                name, operands.Select(operand => Triv(operand, frame)).ToArray());

        private static NanopassException Invalid(object? expression) =>
            new($"Invalid L18 machine term or return address: {expression}.");

        private void Tail(L18.Tail expression, Frame frame)
        {
            switch (expression.Value)
            {
                case L18.Return(var address, var value):
                    object? target = Triv(address, frame);
                    object? result = Triv(value, frame);
                    if (ReferenceEquals(target, this.halt) && this.continuations.Count == 0)
                    {
                        this.answer = result;
                    }
                    else if (this.continuations.TryPop(out var point)
                        && ReferenceEquals(target, point.Address))
                    {
                        this.next = () => point.Resume(result);
                    }
                    else
                    {
                        throw Invalid(target);
                    }

                    break;
                case L18.Tail.App(var procedure, var operands):
                    this.Jump(procedure, operands, frame);
                    break;
                case L18.Tail.If(var test, var yes, var no):
                    this.Predicate(test, frame, value =>
                        this.next = () => this.Tail(value ? yes : no, frame));
                    break;
                case L18.Tail.Begin(var effects, var end):
                    this.Effects(effects, frame, () => this.Tail(end, frame));
                    break;
                default:
                    throw Invalid(expression);
            }
        }

        private void Jump(
            L18.Triv procedure, Seq<L18.Triv> operands, Frame caller,
            Symbol returnLabel = default, ReturnAddress? returnAddress = null)
        {
            var label = (Symbol)Triv(procedure, caller)!;
            var lambda = (L18.Lambda)this.labels[label].Value!;
            var declaration = (L18.Declare)lambda.Dec.Value!;
            if (lambda.XList.Count != operands.Count)
            {
                throw new NanopassException($"L18 procedure arity mismatch: {label}.");
            }

            var callee = new Frame(declaration.XList);
            for (int i = 0; i < operands.Count; i++)
            {
                callee.Define(
                    lambda.XList[i], Triv(operands[i], caller, returnLabel, returnAddress));
            }

            this.next = () => this.Tail(declaration.Tl, callee);
        }

        private void ReturnPoint(
            Symbol label, L18.Application application, Frame frame, Action<object?> resume)
        {
            var address = new ReturnAddress();
            this.continuations.Push(new(address, resume));
            var app = (L18.Application.App)application.Value!;
            this.Jump(app.T0, app.T1List, frame, label, address);
        }

        private void Predicate(L18.Predicate expression, Frame frame, Action<bool> resume)
        {
            switch (expression.Value)
            {
                case L18.True:
                    this.next = () => resume(true);
                    break;
                case L18.False:
                    this.next = () => resume(false);
                    break;
                case L18.Predicate.Primapp(var name, var operands):
                    this.next = () => resume(SchemeValues.IsTrue(Primitive(name, operands, frame)));
                    break;
                case L18.Predicate.SysPrimapp(var name, var operands):
                    this.next = () => resume(SchemeValues.IsTrue(Primitive(name, operands, frame)));
                    break;
                case L18.Predicate.If(var test, var yes, var no):
                    this.next = () => this.Predicate(test, frame, value =>
                        this.next = () => this.Predicate(value ? yes : no, frame, resume));
                    break;
                case L18.Predicate.Begin(var effects, var end):
                    this.Effects(effects, frame, () => this.Predicate(end, frame, resume));
                    break;
                default:
                    throw Invalid(expression);
            }
        }

        private void Effects(Seq<L18.Effect> effects, Frame frame, Action resume)
        {
            int index = 0;
            this.next = Advance;
            void Advance()
            {
                if (index == effects.Count)
                {
                    this.next = resume;
                }
                else
                {
                    this.Effect(effects[index++], frame, Advance);
                }
            }
        }

        private void Effect(L18.Effect expression, Frame frame, Action resume)
        {
            switch (expression.Value)
            {
                case L18.Nop:
                    this.next = resume;
                    break;
                case L18.Effect.Primapp(var name, var operands):
                    _ = Primitive(name, operands, frame);
                    this.next = resume;
                    break;
                case L18.Effect.SysPrimapp(var name, var operands):
                    _ = Primitive(name, operands, frame);
                    this.next = resume;
                    break;
                case L18.Effect.If(var test, var yes, var no):
                    this.Predicate(test, frame, value =>
                        this.next = () => this.Effect(value ? yes : no, frame, resume));
                    break;
                case L18.Effect.Begin(var effects, var end):
                    this.Effects(effects, frame, () => this.Effect(end, frame, resume));
                    break;
                case L18.Set(var name, var rhs):
                    this.Rhs(rhs, frame, value =>
                    {
                        frame.Set(name, value);
                        this.next = resume;
                    });
                    break;
                case L18.Effect.ReturnPoint(var label, var application):
                    this.ReturnPoint(label, application, frame, _ => this.next = resume);
                    break;
                default:
                    throw Invalid(expression);
            }
        }

        private void Rhs(L18.RhsExpr expression, Frame frame, Action<object?> resume)
        {
            switch (expression.Value)
            {
                case L18.Triv trivial:
                    this.next = () => resume(Triv(trivial, frame));
                    break;
                case L18.Primapp(var name, var operands):
                    this.next = () => resume(Primitive(name, operands, frame));
                    break;
                case L18.SysPrimapp(var name, var operands):
                    this.next = () => resume(Primitive(name, operands, frame));
                    break;
                case L18.If(var test, var yes, var no):
                    this.Predicate(test, frame, value =>
                        this.next = () => this.Rhs(value ? yes : no, frame, resume));
                    break;
                case L18.Begin(var effects, var end):
                    this.Effects(effects, frame, () => this.Rhs(end, frame, resume));
                    break;
                case L18.ReturnPoint(var label, var application):
                    this.ReturnPoint(label, application, frame, resume);
                    break;
                default:
                    throw Invalid(expression);
            }
        }

        private sealed class ReturnAddress;

        private sealed record Continuation(ReturnAddress Address, Action<object?> Resume);

        private sealed class Frame(Seq<Symbol> locals)
        {
            private readonly Dictionary<Symbol, object?> slots =
                locals.Distinct().ToDictionary(name => name, _ => (object?)SchemeVoid.Instance);

            internal object? Get(Symbol name) => this.slots[name];

            internal void Define(Symbol name, object? value) => this.slots.Add(name, value);

            internal void Set(Symbol name, object? value)
            {
                if (!this.slots.ContainsKey(name))
                {
                    throw new NanopassException($"Undeclared L18 local: {name}.");
                }

                this.slots[name] = value;
            }
        }
    }
}

internal interface ISchemeProcedure;

internal sealed class SchemePair(object? car, object? cdr)
{
    internal object? Car { get; set; } = car;

    internal object? Cdr { get; set; } = cdr;
}

internal sealed class SchemeVector(object?[] items)
{
    internal object?[] Items { get; } = items;
}

internal sealed class SchemeVoid
{
    private SchemeVoid()
    {
    }

    internal static SchemeVoid Instance { get; } = new();

    public override string ToString() => "#<void>";
}

internal sealed class SchemeClosure(Symbol codeLabel, object?[] freeSlots) : ISchemeProcedure
{
    internal Symbol CodeLabel { get; } = codeLabel;

    internal object?[] FreeSlots { get; } = freeSlots;
}

internal static class SchemeValues
{
    internal static bool IsTrue(object? value) => value is not false;

    internal static object FromDatum(SExpr datum) => datum switch
    {
        SExpr.Int(var value) => value,
        SExpr.Float(var value) => value,
        SExpr.Bool(var value) => value,
        SExpr.Char(var value) => value,
        SExpr.Str(var value) => value,
        SExpr.Sym(var value) => value,
        SExpr.Nil => SExpr.Nil.Instance,
        SExpr.Pair(var head, var tail) => new SchemePair(FromDatum(head), FromDatum(tail)),
        SExpr.Vector(var values) => new SchemeVector(values.Select(FromDatum).ToArray()),
        _ => throw new NanopassException($"Invalid Scheme datum: {datum}."),
    };

    internal static object? ApplyPrimitive(Symbol primitive, object?[] arguments)
    {
        long Number(int index) => (long)arguments[index]!;
        int Index(int index) => checked((int)Number(index));
        SchemePair Pair(int index) => (SchemePair)arguments[index]!;
        SchemeVector Vector(int index) => (SchemeVector)arguments[index]!;
        SchemeClosure Closure(int index) => (SchemeClosure)arguments[index]!;

        switch (primitive.Name)
        {
            case "not": return !IsTrue(arguments[0]);
            case "<": return Number(0) < Number(1);
            case "<=": return Number(0) <= Number(1);
            case "=": return Number(0) == Number(1);
            case "boolean?": return arguments[0] is bool;
            case "char?": return arguments[0] is char;
            case "eq?": return Eq(arguments[0], arguments[1]);
            case "integer?": return arguments[0] is long;
            case "null?": return arguments[0] is SExpr.Nil;
            case "pair?": return arguments[0] is SchemePair;
            case "procedure?": return arguments[0] is ISchemeProcedure;
            case "vector?": return arguments[0] is SchemeVector;
            case "zero?": return Number(0) == 0;
            case "*": return checked(Number(0) * Number(1));
            case "+": return checked(Number(0) + Number(1));
            case "-": return checked(Number(0) - Number(1));
            case "add1": return checked(Number(0) + 1);
            case "car": return Pair(0).Car;
            case "cdr": return Pair(0).Cdr;
            case "char->integer": return (long)(char)arguments[0]!;
            case "cons": return new SchemePair(arguments[0], arguments[1]);
            case "make-vector":
                var items = new object?[Index(0)];
                Array.Fill(items, 0L);
                return new SchemeVector(items);
            case "quotient": return Number(0) / Number(1);
            case "remainder": return Number(0) % Number(1);
            case "sub1": return checked(Number(0) - 1);
            case "vector": return new SchemeVector(arguments);
            case "vector-length": return (long)Vector(0).Items.Length;
            case "vector-ref": return Vector(0).Items[Index(1)];
            case "void": return SchemeVoid.Instance;
            case "set-car!":
                Pair(0).Car = arguments[1];
                return SchemeVoid.Instance;
            case "set-cdr!":
                Pair(0).Cdr = arguments[1];
                return SchemeVoid.Instance;
            case "vector-set!":
                Vector(0).Items[Index(1)] = arguments[2];
                return SchemeVoid.Instance;
            case "closure-ref": return Closure(0).FreeSlots[Index(1)];
            case "make-closure":
                return new SchemeClosure((Symbol)arguments[0]!, new object?[Index(1)]);
            case "procedure-code": return Closure(0).CodeLabel;
            case "closure-set!":
                Closure(0).FreeSlots[Index(1)] = arguments[2];
                return SchemeVoid.Instance;
            case "logand": return Number(0) & Number(1);
            case "sll": return Number(0) << Index(1);
            case "sra": return Number(0) >> Index(1);
            default:
                throw new NanopassException($"Unsupported Scheme primitive: {primitive}.");
        }
    }

    internal static bool Equal(object? left, object? right)
    {
        Stack<(object? Left, object? Right)> pending = new();
        HashSet<(object, object)> visited = [];
        pending.Push((left, right));
        while (pending.TryPop(out var pair))
        {
            if (ReferenceEquals(pair.Left, pair.Right))
            {
                continue;
            }

            switch (pair)
            {
                case (SchemePair a, SchemePair b):
                    if (visited.Add((a, b)))
                    {
                        pending.Push((a.Car, b.Car));
                        pending.Push((a.Cdr, b.Cdr));
                    }

                    break;
                case (SchemeVector a, SchemeVector b) when a.Items.Length == b.Items.Length:
                    if (visited.Add((a, b)))
                    {
                        for (int i = 0; i < a.Items.Length; i++)
                        {
                            pending.Push((a.Items[i], b.Items[i]));
                        }
                    }

                    break;
                case (string a, string b) when string.Equals(a, b, StringComparison.Ordinal):
                case (double first, double second) when first.Equals(second):
                    break;
                default:
                    if (!Eq(pair.Left, pair.Right))
                    {
                        return false;
                    }

                    break;
            }
        }

        return true;
    }

    private static bool Eq(object? left, object? right) =>
        ReferenceEquals(left, right) || left switch
        {
            long number => right is long other && number == other,
            bool boolean => right is bool other && boolean == other,
            char character => right is char other && character == other,
            Symbol symbol => right is Symbol other && symbol == other,
            _ => false,
        };
}
