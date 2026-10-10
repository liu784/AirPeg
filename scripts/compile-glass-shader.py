"""Build original HLSL using the Windows system compiler. No SDK download."""
from pathlib import Path
import ctypes as c, hashlib
root=Path(__file__).resolve().parents[1]/"src/Snapline/Assets/Shaders"
source=(root/"ChromeGlass.hlsl").read_bytes()
dll=c.WinDLL("d3dcompiler_47.dll")
compile=dll.D3DCompile
compile.argtypes=[c.c_void_p,c.c_size_t,c.c_char_p,c.c_void_p,c.c_void_p,c.c_char_p,c.c_char_p,c.c_uint,c.c_uint,c.POINTER(c.c_void_p),c.POINTER(c.c_void_p)]
compile.restype=c.c_long
code=c.c_void_p(); error=c.c_void_p()
hr=compile(source,len(source),b"ChromeGlass.hlsl",None,None,b"main",b"ps_3_0",1<<15,0,c.byref(code),c.byref(error))
def blob(obj):
 table=c.cast(obj,c.POINTER(c.POINTER(c.c_void_p))).contents
 ptr=c.WINFUNCTYPE(c.c_void_p,c.c_void_p)(table[3])(obj)
 size=c.WINFUNCTYPE(c.c_size_t,c.c_void_p)(table[4])(obj)
 return c.string_at(ptr,size)
def release(obj):
 if obj:
  table=c.cast(obj,c.POINTER(c.POINTER(c.c_void_p))).contents
  c.WINFUNCTYPE(c.c_ulong,c.c_void_p)(table[2])(obj)
try:
 if hr<0:raise RuntimeError(blob(error).decode(errors="replace") if error else hex(hr))
 data=blob(code);(root/"ChromeGlass.ps").write_bytes(data)
 print("Compiled",len(data),"bytes; SHA256",hashlib.sha256(data).hexdigest())
finally:release(code);release(error)
