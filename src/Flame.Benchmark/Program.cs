using System;
using Flame.Front.Cli;

namespace Flame.Benchmark
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            var compiler = new BenchmarkCompiler();
            Environment.Exit(compiler.Compile(args));
        }
    }
}
