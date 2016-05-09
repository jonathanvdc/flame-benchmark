# flame-benchmark

## Overview

This repository contains major components. Each component has its own folder:

* `src/Flame.Benchmark/`: the `flame-benchmark` tool, which rewrites regular programs to be executed as benchmarks.
* `src/Harnesses/`: various test harnesses, which `flame-benchmark` uses to rewrite programs.
* `benchmarks/`: a collection of benchmark programs that compare Flame's `-Og` performance to `-O3` performance.

## Running the benchmarks

If you want to run the benchmarks and examine the results for yourself, then you'll first need a couple of

### Prerequisites

You'll need the following external programs:

* A CLR implementation: he .NET Framework ships with all recent versions of Windows, and Linux/Mac OS X users can install Mono.
* A recent C# compiler: `csc` (for Windows users) or `mcs` (for Mono users).
* An MSBuild-compatible build system: `msbuild` (for Windows users) or `xbuild` (for Mono users).
* The NuGet package manager.

If you're a Linux/Mac OS X user, and you don't have Mono installed yet,
then I recommend you follow the Mono project's [useful guide](http://www.mono-project.com/docs/getting-started/install/) to installing Mono.

Regardless of whether you're using Mono or the Windows .NET Framework, you'll also need `dsc` and `compare-test`. These can be acquired by running the following bash script:

```bash
./get-dependencies.sh
```

Next, compile `flame-benchmark`. Again, a script can take care of this:

```bash
./build.sh
```

__Note:__ the script above is Mono-specific. If you want to compile `flame-benchmark` on a Windows computer,
then I suggest you open `src/Flame.Benchmark/Flame.Benchmark.sln` in Visual Studio and compile it in 'Release' mode.

### Actually running the benchmarks

Running the benchmarks is pretty easy, actually: the process is fully automated.
It can be time-consuming, though.
So make sure that you can spare quite a bit of processing power for a while, before running the benchmarks.

If you have used the `get-dependencies.sh` script to install `dsc` and `compare-test`, then the following script will run the benchmarks:

```bash
./run-benchmarks.sh
```

Otherwise, if you already have a version of `dsc` and `compare-test` installed and in your `PATH` environment variable, then the following works, too:

```
cd benchmarks/
compare-test benchmark-all.test
```

After running the benchmarks, the sub-folders of `benchmarks/` will contain `results/` subdirectories. The benchmark results can be found in those `results/` directories.
