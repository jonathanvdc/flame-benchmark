#!/usr/bin/env bash

mkdir -p dependencies
cd dependencies

# Download the dsc binaries
curl -L https://github.com/jonathanvdc/Flame/releases/download/v0.7.5/dsc.zip > dsc.zip
unzip -o dsc.zip -d dsc
rm dsc.zip

# Download the compare-test binaries
curl -L https://github.com/jonathanvdc/compare-test/releases/download/v0.1.0/compare-test.zip > compare-test.zip
unzip -o compare-test.zip -d compare-test
rm compare-test.zip
cd ..
