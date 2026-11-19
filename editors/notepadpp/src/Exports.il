// Native entry points Notepad++ calls (x64), assembled by the .NET Framework's ilasm into deckcheck.dll.
// Each export installs an AssemblyResolve hook (the CLR probes Notepad++'s folder, not this plugin's), then forwards
// through a no-inline method, so DeckcheckNpp.dll is resolved only once the hook exists. Signatures are primitive:
// on x64, NppData (three handles) arrives as a pointer. @VERSION@ is replaced by build.ps1.
.assembly extern mscorlib { .publickeytoken = (B7 7A 5C 56 19 34 E0 89) .ver 4:0:0:0 }
.assembly extern DeckcheckNpp { .ver @VERSION@ }
.assembly deckcheck { .ver @VERSION@ }
.module deckcheck.dll
.corflags 0x00000000
.vtfixup [6] int64 fromunmanaged at VT_01
.data VT_01 = int64[6]

.class public abstract auto ansi sealed beforefieldinit Exports extends [mscorlib]System.Object
{
  .field private static bool hooked

  .method private hidebysig static class [mscorlib]System.Reflection.Assembly
          Resolve(object sender, class [mscorlib]System.ResolveEventArgs args) cil managed
  {
    .maxstack 3
    .locals init (string path)
    ldtoken Exports
    call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
    callvirt instance class [mscorlib]System.Reflection.Assembly [mscorlib]System.Type::get_Assembly()
    callvirt instance string [mscorlib]System.Reflection.Assembly::get_Location()
    call string [mscorlib]System.IO.Path::GetDirectoryName(string)
    ldarg.1
    callvirt instance string [mscorlib]System.ResolveEventArgs::get_Name()
    newobj instance void [mscorlib]System.Reflection.AssemblyName::.ctor(string)
    callvirt instance string [mscorlib]System.Reflection.AssemblyName::get_Name()
    ldstr ".dll"
    call string [mscorlib]System.String::Concat(string, string)
    call string [mscorlib]System.IO.Path::Combine(string, string)
    stloc.0
    ldloc.0
    call bool [mscorlib]System.IO.File::Exists(string)
    brfalse.s NONE
    ldloc.0
    call class [mscorlib]System.Reflection.Assembly [mscorlib]System.Reflection.Assembly::LoadFrom(string)
    ret
  NONE:
    ldnull
    ret
  }

  .method private hidebysig static void Hook() cil managed noinlining
  {
    .maxstack 3
    ldsfld bool Exports::hooked
    brtrue.s DONE
    ldc.i4.1
    stsfld bool Exports::hooked
    call class [mscorlib]System.AppDomain [mscorlib]System.AppDomain::get_CurrentDomain()
    ldnull
    ldftn class [mscorlib]System.Reflection.Assembly Exports::Resolve(object, class [mscorlib]System.ResolveEventArgs)
    newobj instance void [mscorlib]System.ResolveEventHandler::.ctor(object, native int)
    callvirt instance void [mscorlib]System.AppDomain::add_AssemblyResolve(class [mscorlib]System.ResolveEventHandler)
  DONE:
    ret
  }

  .method private static int32 F_isUnicode() cil managed noinlining
  { call int32 [DeckcheckNpp]Deckcheck.Npp.Entry::isUnicode() ret }
  .method private static void F_setInfo(native int p) cil managed noinlining
  { ldarg.0 call void [DeckcheckNpp]Deckcheck.Npp.Entry::setInfo(native int) ret }
  .method private static native int F_getName() cil managed noinlining
  { call native int [DeckcheckNpp]Deckcheck.Npp.Entry::getName() ret }
  .method private static native int F_getFuncsArray(native int n) cil managed noinlining
  { ldarg.0 call native int [DeckcheckNpp]Deckcheck.Npp.Entry::getFuncsArray(native int) ret }
  .method private static native int F_messageProc(unsigned int32 m, native unsigned int w, native int l) cil managed noinlining
  { ldarg.0 ldarg.1 ldarg.2 call native int [DeckcheckNpp]Deckcheck.Npp.Entry::messageProc(unsigned int32, native unsigned int, native int) ret }
  .method private static void F_beNotified(native int p) cil managed noinlining
  { ldarg.0 call void [DeckcheckNpp]Deckcheck.Npp.Entry::beNotified(native int) ret }

  .method public static int32 isUnicode() cil managed
  { .vtentry 1 : 1 .export [1] as isUnicode call void Exports::Hook() call int32 Exports::F_isUnicode() ret }
  .method public static void setInfo(native int p) cil managed
  { .vtentry 1 : 2 .export [2] as setInfo call void Exports::Hook() ldarg.0 call void Exports::F_setInfo(native int) ret }
  .method public static native int getName() cil managed
  { .vtentry 1 : 3 .export [3] as getName call void Exports::Hook() call native int Exports::F_getName() ret }
  .method public static native int getFuncsArray(native int n) cil managed
  { .vtentry 1 : 4 .export [4] as getFuncsArray call void Exports::Hook() ldarg.0 call native int Exports::F_getFuncsArray(native int) ret }
  .method public static native int messageProc(unsigned int32 m, native unsigned int w, native int l) cil managed
  { .vtentry 1 : 5 .export [5] as messageProc call void Exports::Hook() ldarg.0 ldarg.1 ldarg.2 call native int Exports::F_messageProc(unsigned int32, native unsigned int, native int) ret }
  .method public static void beNotified(native int p) cil managed
  { .vtentry 1 : 6 .export [6] as beNotified call void Exports::Hook() ldarg.0 call void Exports::F_beNotified(native int) ret }
}
