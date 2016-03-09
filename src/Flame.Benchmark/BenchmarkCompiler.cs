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
                var nodes = new MarkupNode[] 
                {
                    new MarkupNode(NodeConstants.TextNodeType, "no harness was specified. Use '"),
                    new MarkupNode(NodeConstants.BrightNodeType, "-harness some-harness.flo"),
                    new MarkupNode(NodeConstants.TextNodeType, "' to specify a harness.")
                };

                throw new AbortCompilationException(
                    new LogEntry(
                        AbortCompilationException.FatalErrorEntryTitle, 
                        new MarkupNode("#group", nodes)));
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

            Log.LogEvent(new LogEntry("Harness assembly name", "'" + harnessAsmName + "'"));
            Log.LogEvent(new LogEntry("Status", "writing benchmarking code..."));

            var mainAsm = MainAndOtherAssemblies.Item1;
            var auxAsms = new IAssembly[] { mainAsm }.Concat(MainAndOtherAssemblies.Item2);
            var harnessAsm = auxAsms.FirstOrDefault(item => item.Name == harnessAsmName);
            if (harnessAsm == null)
            {
                throw new AbortCompilationException(new LogEntry(
                    "fatal internal error", 
                    "harness assembly disappeared. No assembly named '" + harnessAsmName + 
                    "' was found among the compiled assemblies."));
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
            //     public static void Main(args...)
            var benchmarkEp = new DescribedBodyMethod(
                "Main", benchmarkClass, PrimitiveTypes.Void, true);
            
            foreach (var parameter in mainFunc.Parameters)
            {
                benchmarkEp.AddParameter(parameter);
            }

            // Synthesize a method body for the entry point function.
            // We want it to look like this:
            // 
            //     typeof(StartBenchmark()) state = StartBenchmark();
            //     int i = iteration_count;
            //     while (i > 0)
            //     {
            //         state = StartIteration(state);
            //         ActualMain(args...);
            //         state = EndIteration(state);
            //         i--;
            //     }
            //     EndBenchmark(state);
            //     return;

            var epBody = new List<IStatement>();

            // Define the state variable, as well as an induction variable.
            var benchVariable = new RegisterVariable("state", harness.StartBenchmarkMethod.ReturnType);
            var inductionVar = new RegisterVariable("i", PrimitiveTypes.Int32);

            // `typeof(StartBenchmark()) state = StartBenchmark();`
            epBody.Add(benchVariable.CreateSetStatement(
                new InvocationExpression(harness.StartBenchmarkMethod, null, new IExpression[0])));

            int iterationCount = Log.Options.GetOption<int>("iterations", 1);

            // `int i = iteration_count;`
            epBody.Add(inductionVar.CreateSetStatement(new Int32Expression(iterationCount)));

            // `state = StartIteration(state);`
            var startIter = harness.StartIterationMethod == null 
                ? EmptyStatement.Instance 
                : benchVariable.CreateSetStatement(new InvocationExpression(
                    harness.StartIterationMethod, null, 
                    new IExpression[] { benchVariable.CreateGetExpression() }));

            // `ActualMain(args...);`
            var runIter = new ExpressionStatement(new InvocationExpression(
                mainFunc, null, 
                benchmarkEp.Parameters.Select((item, i) => new ArgumentVariable(item, i).CreateGetExpression())));

            // `state = EndIteration(state);`
            var endIter = harness.StartIterationMethod == null 
                ? EmptyStatement.Instance 
                : benchVariable.CreateSetStatement(new InvocationExpression(
                    harness.EndIterationMethod, null, 
                    new IExpression[] { benchVariable.CreateGetExpression() }));

            // Create the loop body.
            var loopBody = new BlockStatement(new IStatement[] 
            { 
                startIter, runIter, endIter,
                // `i--;`
                inductionVar.CreateSetStatement(
                    new SubtractExpression(
                        inductionVar.CreateGetExpression(), 
                        new Int32Expression(1)))
            }).Simplify();

            // Create the loop.
            epBody.Add(new WhileStatement(
                new GreaterThanExpression(
                    inductionVar.CreateGetExpression(), 
                    new Int32Expression(0)), 
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
            else
            {
                Log.LogEvent(new LogEntry("Status", HighlightEven("found harness method '", method.FullName, "'.")));
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

        /// <summary>
        /// Retrieves the start-benchmark method from the
        /// given harness class type.
        /// </summary>
        private static IMethod GetStartBenchmarkMethod(IType Type, ICompilerLog Log)
        {
            const string MethodName = "StartBenchmark";

            var results = Type.Methods.Where(item => item.IsStatic && item.Name == MethodName && !item.Parameters.Any()).ToArray();

            if (results.Length == 0)
            {
                throw new AbortCompilationException(new LogEntry(
                    AbortCompilationException.FatalErrorEntryTitle,
                    HighlightEven(
                        "harness class '", Type.FullName, 
                        "' contains no static, parameterless method named '", 
                        MethodName, "'."),
                    Type.GetSourceLocation()));
            }

            if (results.Length > 1)
            {
                throw new AbortCompilationException(new LogEntry(
                    AbortCompilationException.FatalErrorEntryTitle,
                    HighlightEven(
                        "harness class '", Type.FullName, 
                        "' contains more than one static, parameterless method named '", 
                        MethodName, "'."),
                    Type.GetSourceLocation()));
            }

            Log.LogEvent(new LogEntry("Status", HighlightEven("found harness method '", results[0].FullName, "'.")));

            return results[0];
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
            var endIter = GetOptionalHarnessMethod(harnessTy, "EndIteration", stateTy, stateTy, Log);
            var endBench = GetRequiredHarnessMethod(harnessTy, "EndBenchmark", PrimitiveTypes.Void, stateTy, Log);

            return new Harness(startBench, startIter, endBench, endIter);
        }
    }
}

