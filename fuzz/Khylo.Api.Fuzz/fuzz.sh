#!/bin/sh

set -e

dotnet build -c Release ../../Khylo.Api/Khylo.Api.csproj --output bin
sharpfuzz bin/Khylo.Api.dll
cp bin/Khylo.Api.dll .

dotnet build
mkdir -p Findings
AFL_SKIP_BIN_CHECK=1 afl-fuzz -i "Testcases/$1" -o "Findings/$1" -t 5000 ./bin/Debug/net10.0/Khylo.Api.Fuzz "$1"
