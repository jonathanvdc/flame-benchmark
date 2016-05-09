#!/usr/bin/env bash

# Restore Flame.Benchmark's dependencies, then build it.
nuget restore src/Flame.Benchmark/Flame.Benchmark.sln
xbuild /p:Configuration=Release src/Flame.Benchmark/Flame.Benchmark.sln
