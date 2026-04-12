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

using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Memoization.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class MemoizationGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static pi =>
        {
            var src = GenerateSource();
            pi.AddSource("Memoization_CreateMethods.g.cs", SourceText.From(src, Encoding.UTF8));
        });
    }

    private static string GenerateSource()
    {
        var sb = new StringBuilder();

        // Header copied to match original style (license + namespace open curly placement differences are acceptable)
        sb.AppendLine("// Copyright (c) 2022 Zhang Shuai<zhangshuai.ustc@gmail.com>.");
        sb.AppendLine("// All rights reserved.");
        sb.AppendLine("//");
        sb.AppendLine("// This file is part of OneDotNet.");
        sb.AppendLine("//");
        sb.AppendLine("// OneDotNet is free software: you can redistribute it and/or modify it under");
        sb.AppendLine("// the terms of the GNU General Public License as published by the Free");
        sb.AppendLine("// Software Foundation, either version 3 of the License, or (at your option)");
        sb.AppendLine("// any later version.");
        sb.AppendLine("//");
        sb.AppendLine("// OneDotNet is distributed in the hope that it will be useful, but WITHOUT ANY");
        sb.AppendLine("// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS");
        sb.AppendLine("// FOR A PARTICULAR PURPOSE. See the GNU General Public License for more");
        sb.AppendLine("// details.");
        sb.AppendLine("//");
        sb.AppendLine("// You should have received a copy of the GNU General Public License along with");
        sb.AppendLine("// OneDotNet. If not, see <https://www.gnu.org/licenses/>.");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Diagnostics;");
        sb.AppendLine("using Microsoft.Extensions.Caching.Memory;");
        sb.AppendLine();
        sb.AppendLine("namespace Memoization");
        sb.AppendLine("{");
        sb.AppendLine("\t/// <summary>");
        sb.AppendLine("\t/// Provides methods to create memoized versions of functions using Microsoft.Extensions.Caching.Memory.");
        sb.AppendLine("\t/// Memoization is an optimization technique that caches function results to avoid repeated computations.");
        sb.AppendLine("\t/// </summary>");
        sb.AppendLine("\t/// <remarks>");
        sb.AppendLine("\t/// This class supports memoizing functions with up to 16 parameters. The cache key is created using");
        sb.AppendLine("\t/// a tuple of all input parameters. For best performance, ensure parameter types implement proper");
        sb.AppendLine("\t/// equality comparison and have reasonable hash code distributions.");
        sb.AppendLine("\t/// </remarks>");
        sb.AppendLine("\t/// <example>");
        sb.AppendLine("\t/// <code>");
        sb.AppendLine("\t/// // Set up default cache");
        sb.AppendLine("\t/// Memoization.DefaultCache = new MemoryCache(new MemoryCacheOptions());");
        sb.AppendLine("\t/// ");
        sb.AppendLine("\t/// // Create memoized function");
        sb.AppendLine("\t/// Func&lt;int, int&gt; fibonacci = n => n &lt; 2 ? 1 : fibonacci(n-1) + fibonacci(n-2);");
        sb.AppendLine("\t/// var memoizedFib = Memoization.Create(fibonacci);");
        sb.AppendLine("\t/// ");
        sb.AppendLine("\t/// // First call computes result");
        sb.AppendLine("\t/// var result1 = memoizedFib(10); // Computes");
        sb.AppendLine("\t/// // Second call returns cached result");
        sb.AppendLine("\t/// var result2 = memoizedFib(10); // From cache");
        sb.AppendLine("\t/// </code>");
        sb.AppendLine("\t/// </example>");
        sb.AppendLine("\tpublic static partial class Memoization");
        sb.AppendLine("\t{");

        for (int i = 1; i <= 16; i++)
        {
            var typeParams = new List<string>(i);
            var paramList = new List<string>(i);
            var tupleKey = new List<string>(i);
            for (int pIndex = 1; pIndex <= i; pIndex++)
            {
                string tp = $"T{pIndex}";
                typeParams.Add(tp);
                paramList.Add($"{tp} t{pIndex}");
                tupleKey.Add($"t{pIndex}");
            }

            string typeParamCsv = string.Join(", ", typeParams);
            string paramDeclCsv = string.Join(", ", paramList);
            string paramCsv = string.Join(", ", tupleKey);

            void AppendTypeParamsXml()
            {
                for (int pi = 0; pi < typeParams.Count; pi++)
                {
                    sb.AppendLine($"\t\t/// <typeparam name=\"{typeParams[pi]}\">Type of parameter {pi + 1}.</typeparam>");
                }
            }

            // Default cache
            sb.AppendLine($"\t\t/// <summary>");
            sb.AppendLine($"\t\t/// Creates a memoized version of a function with {i} parameter{(i > 1 ? "s" : string.Empty)} using the default cache.");
            sb.AppendLine($"\t\t/// </summary>");
            AppendTypeParamsXml();
            sb.AppendLine("\t\t/// <typeparam name=\"TResult\">Type of the function return value.</typeparam>");
            sb.AppendLine("\t\t/// <param name=\"func\">The function to memoize.</param>");
            sb.AppendLine("\t\t/// <returns>A memoized version of the input function.</returns>");
            sb.AppendLine("\t\t/// <exception cref=\"InvalidOperationException\">Thrown when DefaultCache is null.</exception>");
            sb.AppendLine($"\t\t[DebuggerStepThrough]");
            sb.AppendLine($"\t\tpublic static Func<{typeParamCsv}, TResult> Create<{typeParamCsv}, TResult>(Func<{typeParamCsv}, TResult> func)");
            sb.AppendLine("\t\t{");
            sb.AppendLine($"\t\t\treturn Create(func, DefaultCache ?? throw new InvalidOperationException(\"Memoization.DefaultCache is null\"));");
            sb.AppendLine("\t\t}");
            sb.AppendLine();

            // Default cache + options
            sb.AppendLine($"\t\t/// <summary>");
            sb.AppendLine($"\t\t/// Creates a memoized version of a function with {i} parameter{(i > 1 ? "s" : string.Empty)} using the default cache and specified options.");
            sb.AppendLine($"\t\t/// </summary>");
            AppendTypeParamsXml();
            sb.AppendLine("\t\t/// <typeparam name=\"TResult\">Type of the function return value.</typeparam>");
            sb.AppendLine("\t\t/// <param name=\"func\">The function to memoize.</param>");
            sb.AppendLine("\t\t/// <param name=\"options\">Cache entry options for controlling cache behavior.</param>");
            sb.AppendLine("\t\t/// <returns>A memoized version of the input function.</returns>");
            sb.AppendLine("\t\t/// <exception cref=\"InvalidOperationException\">Thrown when DefaultCache is null.</exception>");
            sb.AppendLine($"\t\t[DebuggerStepThrough]");
            sb.AppendLine($"\t\tpublic static Func<{typeParamCsv}, TResult> Create<{typeParamCsv}, TResult>(Func<{typeParamCsv}, TResult> func, MemoryCacheEntryOptions options)");
            sb.AppendLine("\t\t{");
            sb.AppendLine($"\t\t\treturn Create(func, DefaultCache ?? throw new InvalidOperationException(\"Memoization.DefaultCache is null\"), options);");
            sb.AppendLine("\t\t}");
            sb.AppendLine();

            // Provided cache
            sb.AppendLine($"\t\t/// <summary>");
            sb.AppendLine($"\t\t/// Creates a memoized version of a function with {i} parameter{(i > 1 ? "s" : string.Empty)} using the specified cache.");
            sb.AppendLine($"\t\t/// </summary>");
            AppendTypeParamsXml();
            sb.AppendLine("\t\t/// <typeparam name=\"TResult\">Type of the function return value.</typeparam>");
            sb.AppendLine("\t\t/// <param name=\"func\">The function to memoize.</param>");
            sb.AppendLine("\t\t/// <param name=\"cache\">The memory cache to use for storing results.</param>");
            sb.AppendLine("\t\t/// <returns>A memoized version of the input function.</returns>");
            sb.AppendLine($"\t\tpublic static Func<{typeParamCsv}, TResult> Create<{typeParamCsv}, TResult>(Func<{typeParamCsv}, TResult> func, IMemoryCache cache)");
            sb.AppendLine("\t\t{");
            sb.AppendLine($"\t\t\treturn ({paramDeclCsv}) => cache.GetOrCreate(({paramCsv}), ignored => func({paramCsv}));");
            sb.AppendLine("\t\t}");
            sb.AppendLine();

            // Provided cache + options
            sb.AppendLine($"\t\t/// <summary>");
            sb.AppendLine($"\t\t/// Creates a memoized version of a function with {i} parameter{(i > 1 ? "s" : string.Empty)} using the specified cache and options.");
            sb.AppendLine($"\t\t/// </summary>");
            AppendTypeParamsXml();
            sb.AppendLine("\t\t/// <typeparam name=\"TResult\">Type of the function return value.</typeparam>");
            sb.AppendLine("\t\t/// <param name=\"func\">The function to memoize.</param>");
            sb.AppendLine("\t\t/// <param name=\"cache\">The memory cache to use for storing results.</param>");
            sb.AppendLine("\t\t/// <param name=\"options\">Cache entry options for controlling cache behavior.</param>");
            sb.AppendLine("\t\t/// <returns>A memoized version of the input function.</returns>");
            sb.AppendLine("\t\t/// <remarks>");
            sb.AppendLine("\t\t/// This method manually manages cache entries to ensure proper exception handling.");
            sb.AppendLine("\t\t/// If the function throws an exception, the cache entry is not added.");
            sb.AppendLine("\t\t/// </remarks>");
            sb.AppendLine($"\t\tpublic static Func<{typeParamCsv}, TResult> Create<{typeParamCsv}, TResult>(Func<{typeParamCsv}, TResult> func, IMemoryCache cache, MemoryCacheEntryOptions options)");
            sb.AppendLine("\t\t{");
            sb.AppendLine($"\t\t\treturn ({paramDeclCsv}) =>");
            sb.AppendLine("\t\t\t{\n\t\t\t\tvar key = (" + paramCsv + ");");
            sb.AppendLine("\t\t\t\tif (!cache.TryGetValue<TResult>(key, out var result))");
            sb.AppendLine("\t\t\t\t{\n\t\t\t\t\tvar entry = cache.CreateEntry(key);\n\t\t\t\t\tresult = func(" + paramCsv + ");\n\t\t\t\t\tentry.SetOptions(options);\n\t\t\t\t\tentry.SetValue(result);\n\t\t\t\t\t// need to manually call dispose instead of having a using\n\t\t\t\t\t// in case the factory passed in throws, in which case we\n\t\t\t\t\t// do not want to add the entry to the cache\n\t\t\t\t\tentry.Dispose();\n\t\t\t\t}\n\t\t\t\treturn result;\n\t\t\t};");
            sb.AppendLine("\t\t}");
            sb.AppendLine();
        }

        sb.AppendLine("\t}\n}");
        return sb.ToString();
    }
}
