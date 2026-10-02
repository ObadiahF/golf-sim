#!/bin/sh
# Compile CourseBuilder Runtime + Editor with Unity's bundled Roslyn (no running Editor needed).
# Usage: Tools/course_gen/compile_check.sh   (from the project root)
set -e
UNITY=${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents}
S=$UNITY/Resources/Scripting
OUT=${TMPDIR:-/tmp}/coursebuilder_compile
mkdir -p "$OUT"
INPUT_SYSTEM=${INPUT_SYSTEM_DLL:-/Users/obadiah/Projects/golf-sim/Golf-sim/Library/ScriptAssemblies/Unity.InputSystem.dll}
{
  echo "-nologo -langversion:9 -nostdlib -t:library -nowarn:1701,1702"
  echo "-r:$S/NetStandard/ref/2.1.0/netstandard.dll"
  ls "$S"/Managed/UnityEngine/UnityEngine*.dll "$S"/Managed/UnityEngine/UnityEditor*.dll | sed 's/^/-r:/'
} > "$OUT/refs.rsp"
DOTNET="$S/DotNetSdk/dotnet"
CSC="$S/DotNetSdk/sdk/8.0.318/Roslyn/bincore/csc.dll"
"$DOTNET" "$CSC" @"$OUT/refs.rsp" -r:"$INPUT_SYSTEM" -out:"$OUT/rt.dll" $(find Assets/CourseBuilder/Runtime -name '*.cs')
"$DOTNET" "$CSC" @"$OUT/refs.rsp" -r:"$OUT/rt.dll" -out:"$OUT/ed.dll" $(find Assets/CourseBuilder/Editor -name '*.cs')
echo "CourseBuilder compiles."
