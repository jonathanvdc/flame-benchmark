#!/usr/bin/env bash

cd benchmarks
../dependencies/compare-test/compare-test.exe benchmark-all.test -dsc=$(pwd)/../dependencies/dsc/dsc.exe
cd ..
