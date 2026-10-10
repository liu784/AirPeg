# Original live settings-glass GPU bridge

This small x64 helper belongs to the existing Tendedero Windows/WPF product. It does not contain Codex Flow source/assets, third-party shaders, a native hook, a new UI framework, or an Apple private renderer.

Build with the existing installed MSVC/Windows SDK through scripts/Build-LiveGlass.ps1. Only invocation-local compiler include/library arguments are used. The original HLSL is embedded into the DLL at build time and compiled at runtime by the installed system d3dcompiler_47.dll. /MT avoids a new system runtime installation.

## C ABI (all cdecl, x64)

- void* LiveGlass_Create(HWND window): requires this process's top-level HWND. Returns a context or nullptr. Read LiveGlass_LastError(nullptr) after create failure.
- int LiveGlass_Resize(void* context, int width, int height): returns 1 or a negative HRESULT. WPF must clear its D3DImage backbuffer and release its leased surface **before** resize.
- int LiveGlass_Render(void* context, int screenX, int screenY, int width, int height, float density, int dark): physical screen coordinates/sizes; density 0–1; dark zero/nonzero. Returns 1 when a completed GPU frame may be dirtied in D3DImage, 0 for no fresh/completed frame, or a negative HRESULT.
- IDirect3DSurface9* LiveGlass_Surface9(void* context): **AddRef** lease; caller must Release after its D3DImage is cleared. The texture is D3DFMT_A8R8G8B8.
- void LiveGlass_SetContentRect(void* context, float left, float top, float right, float bottom): retained ABI compatibility no-op; the current clear material has no reading-region mask.
- int LiveGlass_IsPending(void* context): 1 while submitted GPU writes are pending, else 0. WPF keeps its same D3DImage.Lock across ticks until completion; static no-frame ticks release immediately.
- const wchar_t* LiveGlass_LastError(void* context): borrowed diagnostic string, valid until that context changes or is destroyed. nullptr retrieves thread-local create failure.
- void LiveGlass_Destroy(void* context): stop caller ticks, clear WPF backbuffer and release caller's surface lease first. Restores this own HWND's duplication exclusion before native resources release.

Create establishes D3D11 and D3D9Ex on matching adapter LUIDs. Resize creates a D3D9Ex single-mip/default/render-target shared A8R8G8B8 texture and opens it in D3D11; actual BGRA/size/sample format descriptors are checked. The legacy shared handle is not an NT handle and is never passed to CloseHandle.

Every acquired duplication frame is released; pixels are copied to an independently owned GPU texture before release. Pointer-only frames are not copied. No desktop update means no Gaussian/optical draw unless position/material parameters changed. A D3D11_QUERY_EVENT proves GPU completion before returning 1; the UI thread never spins waiting. Caller must keep D3DImage locked while IsPending is 1 so shared-surface GPU writes never cross an unlocked interval. Pending queries exceeding 300ms fail for caller fallback. First submission usually returns 0 and a subsequent tick returns 1.

No render call occurs while the effect is off, minimized, or the D3DImage front buffer is unavailable; that scheduling responsibility belongs to the WPF caller. Cross-monitor movement requests safe context recreation. Rotated outputs, failed duplication/share support, unexpected formats, secure desktop/device/access loss fail explicitly. This initial helper does not capture pixels from a second monitor across a straddling window, does not implement HDR or Apple-engine equivalence, and does not bypass protected capture restrictions.

## Documented capture-exclusion boundary

The helper dynamically resolves Microsoft's WCA_EXCLUDED_FROM_DDA=24 to exclude only its own settings HWND before capture. Missing export or failed set aborts initialization; there is no capture loop without exclusion and no hook. This documented attribute prevents duplication recursion while leaving other screenshot methods available.

Microsoft still marks the general SetWindowCompositionAttribute function as not recommended. This compatibility limitation must be reported, and the real desktop probe must confirm successful exclusion actually yields the background rather than a black hole or recursive image. A successful function return alone is not final visual validation.

- https://learn.microsoft.com/en-us/windows/win32/dwm/windowcompositionattrib
- https://learn.microsoft.com/en-us/windows/win32/dwm/setwindowcompositionattribute
- https://github.com/microsoft/WPFDXInterop/blob/master/src/Microsoft.Wpf.Interop.DirectX/SurfaceDevice9.cpp
- https://learn.microsoft.com/en-us/windows/win32/api/dxgi/nf-dxgi-idxgiresource-getsharedhandle

The Microsoft interop sample's small API pattern is cited to establish format compatibility; its framework is not copied or installed. All optical shader expressions here are original. SDK and compiler binaries are build inputs, not redistribution contents.

Compilation alone does not establish native rendering, realtime updating, performance, or user/ChatGPT visual acceptance.

## Current clear material (latest user direction)

The latest user correction distinguishes naturally visible current background text from incorrect stale/recursive background imagery. The user requested clear glass without a reading-panel mask, extra white/dark opacity, or forced text suppression. Earlier protected reading-region variants were not visually accepted and are superseded.

The current original pipeline samples the latest captured desktop twice: as its unblurred source and as a continuous softening field. First, a 4x4 area prefilter reduces each window crop to quarter resolution. Adjacent Gaussian taps with exact bilinear pairing then provide a uniform sigma of 4-12 physical pixels at 100% DPI, scaled for the current window size. Composition mixes the original desktop RGB and the softened RGB with a weight of 0.08 + 0.52 * density. The original narrow edge refraction and restrained neutral Fresnel highlight remain. The D3DImage shared surface retains full window width/height.

There is no content-area rectangle, separate header/navigation blur, white/dark neutral tint, luminance floor/ceiling, or special protection region. Naturally visible background glyphs are permitted by this latest direction. Alpha 1 is the complete composed desktop color, not a white covering layer. Dark mode controls foreground WPF styling; the native clear material deliberately preserves the real background's colors/luminance in either mode.

These numbers are original, testable product approximation parameters, not published Apple rendering constants or evidence of access to Apple private rendering. The current material uses three quarter-size targets and four GPU passes (prefilter, horizontal blur, vertical blur, composition). It is simpler and lighter than the superseded multi-region variant.

Repeated SetContentRect calls are now compatibility no-ops, so they cannot trigger continuous idle rendering. A same-size Resize still marks parameters dirty to publish a new frame after D3DImage rebind. Position/density changes also redraw from the latest captured GPU frame, even when the desktop source has no new frame.

The old v3 native source, DLL, documentation, and hashes are preserved outside the product tree under evidence/p4-real-composition/native-v3-before-clear-20261010. Existing actual v1/v2/v3 screenshots and reports remain historical evidence; they are not user approval of the current material.

## Historical defects and required native gates

The first actual matrix exposed grid/moire from sparse full-resolution Gaussian sampling. The 4x4 prefilter and continuous adjacent taps fixed that actual sampling defect. Later multi-region protection introduced a visually distinct reading-panel rectangle and excessive background suppression. The latest clear material removes those regions entirely.

The second actual matrix also showed nearly frozen native frame counters across background/parameter changes. It did not prove realtime updates. Root owns the independent dispatcher polling and D3DImage lock scheduling fix; the native helper retains bounded non-spinning GPU completion. A genuine new desktop matrix must verify background A/B changes, close/reopen, glass off/on, movement, density response, no self-recursion, and no stale image. No final visual PASS follows from source/build checks.
