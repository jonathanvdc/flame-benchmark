using System;
using Flame.Front.Cli;
using System.Collections.Generic;
using Flame.Front.Projects;
using Flame.Front.Options;
using Flame.Compiler;
using Pixie;
using System.Linq;
using Flame.Front;
using System.Threading.Tasks;
using Flame.Build;
using Flame.Compiler.Statements;
using Flame.Compiler.Variables;
using Flame.Compiler.Expressions;

namespace Flame.Benchmark
{
    public class BenchmarkCompiler : ConsoleCompiler
    {
        public BenchmarkCompiler()
            : base("flame-benchmark", "the Flame benchmark writer", "https://github.com/jonathanvdc/flame-benchmark/releases")
        { }

        private string harnessAsmName;

        protected override IEnumerable<ProjectDependency> GetExtraProjects(BuildArguments Args, ICompilerLog Log)
        {
            if (Args.GetOption<bool>("no-harness", false))
            {
                return Enumerable.Empty<ProjectDependency>();
            }

            string harnessName = Args.GetOption<string>("harness", null);

            if (string.IsNullOrWhiteSpace(harnessName))
            {
                throw new AbortCompilationException(
                    new LogEntry(
                        AbortCompilationException.FatalErrorEntryTitle, 
                        HighlightEven(
                            "no harness was specified. Use '",
                            "-harness some-harness.flo", 
                            "' to specify a harness.")));
            }
                
            var pathIdent = new PathIdentifier(harnessName);

            var parsedProj = ParseProject(pathIdent, Args, Log);
            harnessAsmName = parsedProj.Item1.Project.AssemblyName;

            return new ProjectDependency[] 
            {
                new ProjectDependency(parsedProj.Item1, parsedProj.Item2)
            };
        }

        private static MarkupNode HighlightEven(params string[] Args)
        {
            var results = new MarkupNode[Args.Length];
            for (int i = 0; i < Args.Length; i++)
            {
                results[i] = new MarkupNode(
                    i % 2 == 0 ? NodeConstants.TextNodeType : NodeConstants.BrightNodeType,
                    Args[i]);
            }
            return new MarkupNode("#group", results);
        }

        protected override async Task<Tuple<IAssembly, IEnumerable<IAssembly>>> RewriteAssembliesAsync(
            Tuple<IAssembly, IEnumerable<IAssembly>> MainAndOtherAssemblies, 
            Task<IBinder> BinderTask, ICompilerLog Log)
        {
            if (harnessAsmName == null)
            {
                Log.LogEvent(new LogEntry("Status", "no harness assembly."));
                return MainAndOtherAssemblies;
            }

            Log.LogEvent(new LogEntry("Status", HighlightEven("harness assembly name: '", harnessAsmName, "'")));
            Log.LogEvent(new LogEntry("Status", "writing benchmarking code..."));

            var mainAsm = MainAndOtherAssemblies.Item1;
            var auxAsms = new IAssembly[] { mainAsm }.Concat(MainAndOtherAssemblies.Item2);
            var harnessAsm = auxAsms.FirstOrDefault(item => item.Name == harnessAsmName);
            if (harnessAsm == null)
            {
                throw new AbortCompilationException(new LogEntry(
                    "fatal internal error", 
                    HighlightEven(
                        "harness assembly disappeared. No assembly named '", harnessAsmName,
                        "' was found among the compiled assemblies.")));
            }

            var mainFunc = mainAsm.GetEntryPoint();
            if (mainFunc == null)
            {
                throw new AbortCompilationException(new LogEntry(
                    AbortCompilationException.FatalErrorEntryTitle,
                    "entry point missing. At least one input assembly " +
                    "should have an entry point function."));
            }

            var harness = ExtractHarness(harnessAsm, Log);

            var binder = await BinderTask;
            var benchAsm = new DescribedAssembly("Benchmark", binder.Environment);
            var mainNs = new MainNamespace(benchAsm);

            // Define a class:
            //     public static class __benchmark
            var benchmarkClass = new DescribedType("__benchmark", mainNs);
            benchmarkClass.AddAttribute(new AccessAttribute(AccessModifier.Public));
            benchmarkClass.AddAttribute(PrimitiveAttributes.Instance.StaticTypeAttribute);

            // Define an entry point function:
            //     public static void Main(string[] Args)
            var benchmarkEp = new DescribedBodyMethod(
                "Main", benchmarkClass, PrimitiveTypes.Void, true);

            benchmarkEp.AddParameter(new DescribedParameter("Args", PrimitiveTypes.String.MakeArrayType(1)));
            var benchEpArg = new ArgumentVariable(benchmarkEp.Parameters.Single(), 0).CreateGetExpression();

            // Synthesize a method body for the entry point function.
            // We want it to look like this:
            // 
            //     typeof(StartBenchmark()) state = StartBenchmark(args, iteration_count);
            //     while (IsRunning(state))
            //     {
            //         state = StartIteration(state);
            //         GetIterationArguments(state) |> ActualMain;
            //         state = EndIteration(state);
            //     }
            //     EndBenchmark(state);
            //     return;

            var epBody = new List<IStatement>();

            // Define the state variable, as well as an induction variable.
            var benchVariable = new RegisterVariable("state", harness.StartBenchmarkMethod.ReturnType);

            int iterationCount = Log.Options.GetOption<int>("iterations", 1);

            // `typeof(StartBenchmark()) state = StartBenchmark();`
            epBody.Add(benchVariable.CreateSetStatement(
                CreatePartialInvocation(
                    harness.StartBenchmarkMethod, 
                    new IExpression[] { benchEpArg, new Int32Expression(iterationCount) })));

            // `state = StartIteration(state);`
            var startIter = harness.StartIterationMethod == null 
                ? EmptyStatement.Instance 
                : benchVariable.CreateSetStatement(new InvocationExpression(
                    harness.StartIterationMethod, null, 
                    new IExpression[] { benchVariable.CreateGetExpression() }));

            // `GetIterationArguments(state) |> ActualMain;`
            var runIter = new ExpressionStatement(CreateMainInvocation(
                mainFunc, harness, benchVariable.CreateGetExpression()));

            // `state = EndIteration(state);`
            var endIter = harness.StartIterationMethod == null 
                ? EmptyStatement.Instance 
                : benchVariable.CreateSetStatement(new InvocationExpression(
                    harness.EndIterationMethod, null, 
                    new IExpression[] { benchVariable.CreateGetExpression() }));

            // Create the loop body.
            var loopBody = new BlockStatement(new IStatement[] 
            { 
                startIter, runIter, endIter
            }).Simplify();

            // Create the loop.
            epBody.Add(new WhileStatement(
                new InvocationExpression(
                    harness.IsRunningMethod, null,
                    new IExpression[] { benchVariable.CreateGetExpression() }),
                loopBody));

            // `EndBenchmark(state);`
            epBody.Add(new ExpressionStatement(new InvocationExpression(
                harness.EndBenchmarkMethod, null, 
                new IExpression[] { benchVariable.CreateGetExpression() })));
            // `return;`
            epBody.Add(new ReturnStatement());
            benchmarkEp.Body = new BlockStatement(epBody).Simplify();

            benchmarkClass.AddMethod(benchmarkEp);
            benchAsm.AddType(benchmarkClass);
            mainNs.Types.Add(benchmarkClass);
            benchAsm.EntryPoint = benchmarkEp;

            Log.LogEvent(new LogEntry("Status", "done writing benchmarking code"));

            return Tuple.Create<IAssembly, IEnumerable<IAssembly>>(benchAsm, auxAsms);
        }


        /// <summary>
        /// Describes a harness method signature.
        /// </summary>
        private static string DescribeHarnessMethodSignature(string Name, IType ReturnType, IType ParameterType)
        {
            var namer = new TypeNamerBase();
            return "static " + namer.Convert(ReturnType) + " " + Name + "(" + namer.Convert(ParameterType) + ")";
        }

        /// <summary>
        /// Retrieves an optional harness method from 
        /// the given harness class type.
        /// </summary>
        private static IMethod GetOptionalHarnessMethod(
            IType Type, string Name, IType ReturnType, 
            IType ParameterType, ICompilerLog Log)
        {
            var method = Type.Methods.GetMethod(Name, true, ReturnType, new IType[] { ParameterType });

            if (method == null && Type.Methods.Any(item => item.Name == Name))
            {
                Log.LogWarning(new LogEntry(
                    "potential signature mismatch",
                    HighlightEven(
                        "harness class '", Type.FullName, 
                        "' does not contain a method with signature '", 
                        DescribeHarnessMethodSignature(Name, ReturnType, ParameterType), 
                        "', but does contain methods with the same name ('", 
                        Name, "')."),
                    Type.GetSourceLocation()));
            }
            else if (method == null)
            {
                Log.LogEvent(new LogEntry(
                    "Status", 
                    HighlightEven(
                        "did not find optional harness method '", 
                        MemberExtensions.CombineNames(Type.FullName, Name), "'.")));
            }
            else
            {
                Log.LogEvent(new LogEntry(
                    "Status", 
                    HighlightEven(
                        "found harness method '", method.FullName, "'.")));
            }

            return method;
        }

        /// <summary>
        /// Retrieves a required harness method from 
        /// the given harness class type.
        /// </summary>
        private static IMethod GetRequiredHarnessMethod(
            IType Type, string Name, IType ReturnType, 
            IType ParameterType, ICompilerLog Log)
        {
            var method = GetOptionalHarnessMethod(Type, Name, ReturnType, ParameterType, Log);
            if (method == null)
            {
                
                throw new AbortCompilationException(new LogEntry(
                    AbortCompilationException.FatalErrorEntryTitle,
                    HighlightEven(
                        "harness class '", Type.FullName, 
                        "' contains no method with signature '", 
                        DescribeHarnessMethodSignature(Name, ReturnType, ParameterType), "'."),
                    Type.GetSourceLocation()));
            }
            return method;
        }

        private static bool IsPartialMatch(IType[] Values, IType[] Template)
        {
            if (Values.Length > Template.Length)
                return false;

            for (int i = 0; i < Values.Length; i++)
            {
                if (!Values[i].Equals(Template[i]))
                    return false;
            }

            return true;
        }

        private static IExpression CreatePartialInvocation(IMethod Method, params IExpression[] Args)
        {
            var argList = Method.Parameters.Zip(Args, Tuple.Create).Select(item => item.Item2).ToArray();
            return new InvocationExpression(Method, null, argList);
        }

        /// <summary>
        /// Retrieves the start-benchmark method from the
        /// given harness class type.
        /// </summary>
        private static IMethod GetStartBenchmarkMethod(IType Type, ICompilerLog Log)
        {
            const string MethodName = "StartBenchmark";

            var argTys = new IType[] { PrimitiveTypes.String.MakeArrayType(1), PrimitiveTypes.Int32 };

            var results = Type.Methods.Where(item => 
            {
                if (item.IsStatic && item.Name == MethodName)
                {
                    return IsPartialMatch(
                        item.Parameters.Select(p => p.ParameterType).ToArray(), 
                        argTys);
                }
                else
                {
                    return false;
                }
            }).ToArray();

            if (results.Length == 0)
            {
                throw new AbortCompilationException(new LogEntry(
                    AbortCompilationException.FatalErrorEntryTitle,
                    HighlightEven(
                        "harness class '", Type.FullName, 
                        "' contains no static method named '", 
                        MethodName, "' that takes at most a '", 
                        "string[]", " and an '", "int", "' parameter."),
                    Type.GetSourceLocation()));
            }

            if (results.Length > 1)
            {
                throw new AbortCompilationException(new LogEntry(
                    AbortCompilationException.FatalErrorEntryTitle,
                    HighlightEven(
                        "harness class '", Type.FullName, 
                        "' contains more than one static method named '", 
                        MethodName, "' that takes at most a '", 
                        "string[]", " and an '", "int", "' parameter."),
                    Type.GetSourceLocation()));
            }

            Log.LogEvent(new LogEntry("Status", HighlightEven("found harness method '", results[0].FullName, "'.")));

            return results[0];
        }

        /// <summary>
        /// Retrieves the get-iteration-arguments method from the
        /// given harness class type.
        /// </summary>
        private static IMethod FindGetIterationArgumentsMethod(IType HarnessType, IType StateType, ICompilerLog Log)
        {
            const string MethodName = "GetIterationArguments";

            var argTys = new IType[] { StateType };

            var results = HarnessType.Methods.Where(item => 
            {
                if (item.IsStatic && item.Name == MethodName)
                {
                    return IsPartialMatch(
                        argTys,
                        item.Parameters.Select(p => p.ParameterType).ToArray());
                }
                else
                {
                    return false;
                }
            }).ToArray();

            if (results.Length == 0)
            {
                throw new AbortCompilationException(new LogEntry(
                    AbortCompilationException.FatalErrorEntryTitle,
                    HighlightEven(
                        "harness class '", StateType.FullName, 
                        "' contains no static method named '", 
                        MethodName, "' that takes at least a '", 
                        HarnessType.FullName, "' parameter."),
                    HarnessType.GetSourceLocation()));
            }

            if (results.Length > 1)
            {
                throw new AbortCompilationException(new LogEntry(
                    AbortCompilationException.FatalErrorEntryTitle,
                    HighlightEven(
                        "harness class '", StateType.FullName, 
                        "' contains more than one static method named '", 
                        MethodName, "' that takes at least a '", 
                        HarnessType.FullName, "' parameter."),
                    HarnessType.GetSourceLocation()));
            }

            Log.LogEvent(new LogEntry("Status", HighlightEven("found harness method '", results[0].FullName, "'.")));

            return results[0];
        }

        private static IExpression CreateMainInvocation(
            IMethod MainMethod, Harness Harness, IExpression BenchmarkState)
        {
            var getIterFunc = Harness.GetIterationArgumentsMethod;

            var init = new List<IStatement>();
            var mainArgs = new List<IExpression>();
            var getIterArgs = new List<IExpression>();
            getIterArgs.Add(BenchmarkState);

            foreach (var item in getIterFunc.Parameters.Skip(1))
            {
                var ty = item.ParameterType;
                if (ty.GetIsPointer() &&
                    ty.AsPointerType().PointerKind.Equals(PointerKind.ReferencePointer))
                {
                    if (!item.HasAttribute(PrimitiveAttributes.Instance.OutAttribute.AttributeType))
                    {
                        throw new AbortCompilationException(new LogEntry(
                            AbortCompilationException.FatalErrorEntryTitle,
                            HighlightEven(
                                "parameter '", item.Name, "' of harness method '", getIterFunc.FullName, 
                                "' was not an '", "out", "' parameter."),
                            item.GetSourceLocation()));
                    }

                    var local = new LocalVariable(ty.AsPointerType().ElementType);
                    getIterArgs.Add(local.CreateAddressOfExpression());
                    mainArgs.Add(local.CreateGetExpression());
                }
                else
                {
                    throw new AbortCompilationException(new LogEntry(
                        AbortCompilationException.FatalErrorEntryTitle,
                        HighlightEven(
                            "parameter '", item.Name, "' of harness method '", getIterFunc.FullName, 
                            "' had type '", ty.Name, "'. Expected a reference pointer type, i.e. '", 
                            ty.MakePointerType(PointerKind.ReferencePointer).Name, "'."),
                        item.GetSourceLocation()));
                }
            }

            var getIterCall = new InvocationExpression(
                getIterFunc, null, getIterArgs);
            
            if (!getIterFunc.ReturnType.IsEquivalent(PrimitiveTypes.Void))
                mainArgs.Add(getIterCall);
            else
                init.Add(new ExpressionStatement(getIterCall));

            var actualArgs = new List<IExpression>();
            foreach (var argPair in MainMethod.Parameters.Zip(mainArgs, Tuple.Create))
            {
                if (!argPair.Item1.ParameterType.IsEquivalent(argPair.Item2.Type))
                {
                    throw new AbortCompilationException(new LogEntry(
                        AbortCompilationException.FatalErrorEntryTitle,
                        HighlightEven(
                            "parameter '", argPair.Item1.Name, "' of '", MainMethod.Name, 
                            "' method had type '", argPair.Item1.ParameterType.Name, 
                            "', but the corresponding result value from '", getIterFunc.Name, 
                            "' had type '", argPair.Item2.Type.Name, "'."),
                        argPair.Item1.GetSourceLocation()));
                }

                actualArgs.Add(argPair.Item2);
            }

            return new InitializedExpression(
                new BlockStatement(init),
                new InvocationExpression(MainMethod, null, actualArgs));
        }

        /// <summary>
        /// Extracts test harness data from the given harness assembly.
        /// </summary>
        private static Harness ExtractHarness(IAssembly HarnessAssembly, ICompilerLog Log)
        {
            const string HarnessTypeName = "Harness";

            var harnessTy = HarnessAssembly.CreateBinder().BindType(HarnessTypeName);

            if (harnessTy == null)
            {
                throw new AbortCompilationException(new LogEntry(
                    AbortCompilationException.FatalErrorEntryTitle,
                    HighlightEven(
                        "harness assembly '", HarnessAssembly.Name, 
                        "' did not contain a type whose full name was '", 
                        HarnessTypeName, "'.")));
            }

            var startBench = GetStartBenchmarkMethod(harnessTy, Log);
            var stateTy = startBench.ReturnType;
            var startIter = GetOptionalHarnessMethod(harnessTy, "StartIteration", stateTy, stateTy, Log);
            var isRunning = GetRequiredHarnessMethod(harnessTy, "IsRunning", PrimitiveTypes.Boolean, stateTy, Log);
            var getIterationArgs = FindGetIterationArgumentsMethod(harnessTy, stateTy, Log);
            var endIter = GetOptionalHarnessMethod(harnessTy, "EndIteration", stateTy, stateTy, Log);
            var endBench = GetRequiredHarnessMethod(harnessTy, "EndBenchmark", PrimitiveTypes.Void, stateTy, Log);

            return new Harness(startBench, isRunning, startIter, getIterationArgs, endBench, endIter);
        }
    }
}

