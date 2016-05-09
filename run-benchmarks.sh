#!/usr/bin/env bash

cd benchmarks
mono ../dependencies/compare-test/compare-test.exe benchmark-all.test -dsc="mono $(pwd)/../dependencies/dsc/dsc.exe"
cd ..
