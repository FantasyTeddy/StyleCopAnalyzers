﻿// Copyright (c) Tunnel Vision Laboratories, LLC. All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace StyleCop.Analyzers.PrivateAnalyzers
{
    using System;
    using System.Collections.Immutable;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Microsoft.CodeAnalysis;

    [Generator]
    internal sealed class DerivedTestGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var testData = context.CompilationProvider.Select((compilation, cancellationToken) =>
            {
                var currentAssemblyName = compilation.AssemblyName ?? string.Empty;
                if (!Regex.IsMatch(currentAssemblyName, @"^StyleCop\.Analyzers\.Test\.CSharp\d+$"))
                {
                    // This is not a test project where derived test classes are expected
                    return null;
                }

                var currentVersion = int.Parse(currentAssemblyName["StyleCop.Analyzers.Test.CSharp".Length..]);
                var currentTestString = "CSharp" + currentVersion;
                var previousTestString = currentVersion switch
                {
                    7 => string.Empty,
                    _ => "CSharp" + (currentVersion - 1).ToString(),
                };
                var previousAssemblyName = previousTestString switch
                {
                    "" => "StyleCop.Analyzers.Test",
                    _ => "StyleCop.Analyzers.Test." + previousTestString,
                };

                return new TestData(previousTestString, previousAssemblyName, currentTestString, currentAssemblyName);
            });

            var testTypes = context.CompilationProvider.Combine(testData).SelectMany((compilationAndTestData, cancellationToken) =>
            {
                var (compilation, testData) = compilationAndTestData;
                if (testData is null)
                {
                    return ImmutableArray<string>.Empty;
                }

                var previousAssembly = compilation.Assembly.Modules.First().ReferencedAssemblySymbols.First(
                    symbol => symbol.Identity.Name == testData.PreviousAssemblyName);
                if (previousAssembly is null)
                {
                    return ImmutableArray<string>.Empty;
                }

                var collector = new TestClassCollector(testData.PreviousTestString);
                var previousTests = collector.Visit(previousAssembly);
                return previousTests.ToImmutableArray();
            });

            context.RegisterSourceOutput(
                testTypes.Combine(testData),
                (context, testTypeAndData) =>
                {
                    var (testType, testData) = testTypeAndData;
                    if (testData is null)
                    {
                        throw new InvalidOperationException("Not reachable");
                    }

                    string expectedTest;
                    if (testData.PreviousTestString is "")
                    {
                        expectedTest = testType.Replace(testData.PreviousAssemblyName, testData.CurrentAssemblyName).Replace("UnitTests", testData.CurrentTestString + "UnitTests");
                    }
                    else
                    {
                        expectedTest = testType.Replace(testData.PreviousTestString, testData.CurrentTestString);
                    }

                    var lastDot = testType.LastIndexOf('.');
                    var baseNamespaceName = testType["global::".Length..lastDot];
                    var baseTypeName = testType[(lastDot + 1)..];

                    lastDot = expectedTest.LastIndexOf('.');
                    var namespaceName = expectedTest["global::".Length..lastDot];
                    var typeName = expectedTest[(lastDot + 1)..];
                    var content =
                        $@"// <auto-generated/>

#nullable enable

namespace {namespaceName};

using {baseNamespaceName};

public partial class {typeName}
    : {baseTypeName}
{{
}}
";

                    context.AddSource(
                        typeName + ".cs",
                        content);
                });
        }

        private sealed record TestData(string PreviousTestString, string PreviousAssemblyName, string CurrentTestString, string CurrentAssemblyName);

        private sealed class TestClassCollector : SymbolVisitor<ImmutableSortedSet<string>>
        {
            private readonly string testString;

            public TestClassCollector(string testString)
            {
                this.testString = testString;
            }

            public override ImmutableSortedSet<string> Visit(ISymbol? symbol)
                => base.Visit(symbol) ?? throw new InvalidOperationException("Not reachable");

            public override ImmutableSortedSet<string>? DefaultVisit(ISymbol symbol)
                => ImmutableSortedSet<string>.Empty;

            public override ImmutableSortedSet<string> VisitAssembly(IAssemblySymbol symbol)
            {
                return this.Visit(symbol.GlobalNamespace);
            }

            public override ImmutableSortedSet<string> VisitNamespace(INamespaceSymbol symbol)
            {
                var result = ImmutableSortedSet<string>.Empty;
                foreach (var member in symbol.GetMembers())
                {
                    result = result.Union(this.Visit(member)!);
                }

                return result;
            }

            public override ImmutableSortedSet<string> VisitNamedType(INamedTypeSymbol symbol)
            {
                if (this.testString is "")
                {
                    if (symbol.Name.EndsWith("UnitTests"))
                    {
                        return ImmutableSortedSet.Create(symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                    }
                    else
                    {
                        return ImmutableSortedSet<string>.Empty;
                    }
                }
                else if (symbol.Name.Contains(this.testString))
                {
                    return ImmutableSortedSet.Create(symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                }
                else
                {
                    return ImmutableSortedSet<string>.Empty;
                }
            }
        }
    }
}
