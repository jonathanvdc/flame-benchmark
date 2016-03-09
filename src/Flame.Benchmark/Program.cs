using System;
using Flame.Front.Cli;

namespace Flame.Benchmark
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            var compiler = new ConsoleCompiler("flame-benchmark", "the Flame benchmark writer", "https://github.com/jonathanvdc/flame-benchmark/releases");
            Environment.Exit(compiler.Compile(args));
        }
    }
}
