// Original small GPU bridge for Tendedero Windows.
// System D3D11/DXGI desktop capture -> original Gaussian/optical shader ->
// D3D9Ex shared BGRA surface -> WPF D3DImage. No hook or external framework.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d11.h>
#include <d3d9.h>
#include <dxgi1_2.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <memory>
#include <vector>
#include <mutex>
#include <map>
#include <atomic>
#include <sstream>
#include <string>
#include "ShaderSource.h"

using Microsoft::WRL::ComPtr;
static std::atomic<int> liveContexts{0};
static thread_local std::wstring globalError = L"No native context.";

struct CompositionData { int attribute; void* data; SIZE_T size; };
using SetCompositionFn = BOOL(WINAPI*)(HWND, const CompositionData*);

struct Constants
{
    float frame[4];
    float crop[4];
    float blur[4];
    float style[4];
    float content[4];
};
static_assert(sizeof(Constants) == 80, "HLSL constant buffer layout");

struct Target
{
    ComPtr<ID3D11Texture2D> texture;
    ComPtr<ID3D11RenderTargetView> rtv;
    ComPtr<ID3D11ShaderResourceView> srv;
};

struct LiveGlass
{
    HWND window = nullptr;
    SetCompositionFn setComposition = nullptr;
    bool excluded = false;
    bool frameHeld = false;
    bool hasLatest = false;
    bool pending = false;
    ULONGLONG pendingAt = 0;
    bool parametersDirty = true;
    int width = 0, height = 0;
    int lastX = 0, lastY = 0, lastDark = -1;
    float lastDensity = -1;

    DXGI_OUTPUT_DESC outputDesc{};
    DXGI_OUTDUPL_DESC duplicationDesc{};
    ComPtr<IDXGIAdapter1> adapter;
    ComPtr<IDXGIOutput1> output;
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> immediate;
    ComPtr<IDXGIOutputDuplication> duplication;
    ComPtr<IDirect3D9Ex> api9;
    ComPtr<IDirect3DDevice9Ex> device9;
    ComPtr<IDirect3DTexture9> texture9;
    ComPtr<IDirect3DSurface9> surface9;
    ComPtr<ID3D11Texture2D> shared11;
    ComPtr<ID3D11RenderTargetView> sharedRtv;
    ComPtr<ID3D11Texture2D> latest;
    ComPtr<ID3D11ShaderResourceView> latestSrv;
    ComPtr<ID3D11Texture2D> previous;
    ComPtr<ID3D11ShaderResourceView> previousSrv;
    ComPtr<ID3D11PixelShader> compareShader;
    ComPtr<ID3D11Predicate> differentPixels;
    ComPtr<ID3D11BlendState> noColorWrites;
    Target comparisonTarget;
    bool conditionalPending = false;
    uint64_t captures = 0, outsideSkips = 0, equalSkips = 0, draws = 0;
    std::vector<BYTE> metadata;
    ComPtr<ID3D11VertexShader> vertex;
    ComPtr<ID3D11PixelShader> downsampleShader;
    ComPtr<ID3D11PixelShader> blurShader;
    ComPtr<ID3D11PixelShader> composeShader;
    ComPtr<ID3D11Buffer> constants;
    ComPtr<ID3D11SamplerState> sampler;
    ComPtr<ID3D11RasterizerState> rasterizer;
    ComPtr<ID3D11Query> completed;
    Target prefiltered, intermediate, softened;
    std::wstring error;

    LiveGlass() { ++liveContexts; }
    ~LiveGlass()
    {
        --liveContexts;
        if (frameHeld && duplication) duplication->ReleaseFrame();
        if (immediate) { immediate->ClearState(); immediate->Flush(); }
        // Restore only our original own-HWND duplication flag.
        if (excluded && setComposition && IsWindow(window))
        {
            DWORD process = 0;
            GetWindowThreadProcessId(window, &process);
            if (process == GetCurrentProcessId())
            {
                BOOL enabled = FALSE;
                CompositionData data{ 24, &enabled, sizeof(enabled) };
                setComposition(window, &data);
            }
        }
    }

    HRESULT fail(HRESULT hr, const wchar_t* operation)
    {
        std::wostringstream text;
        text << operation << L" (HRESULT 0x" << std::hex << static_cast<uint32_t>(hr) << L")";
        error = text.str();
        globalError = error;
        return hr;
    }

    HRESULT init(HWND hwnd)
    {
        window = hwnd;
        DWORD process = 0;
        GetWindowThreadProcessId(hwnd, &process);
        if (!IsWindow(hwnd) || process != GetCurrentProcessId() || GetAncestor(hwnd, GA_ROOT) != hwnd)
            return fail(E_INVALIDARG, L"Create requires a top-level window owned by this process");
        // The documented WCA_EXCLUDED_FROM_DDA attribute is intentionally
        // isolated. Missing export / failed exclusion forbids the capture loop.
        setComposition = reinterpret_cast<SetCompositionFn>(
            GetProcAddress(GetModuleHandleW(L"user32.dll"), "SetWindowCompositionAttribute"));
        if (!setComposition) return fail(HRESULT_FROM_WIN32(ERROR_PROC_NOT_FOUND), L"Capture-exclusion export unavailable");
        BOOL enabled = TRUE;
        CompositionData exclusion{ 24, &enabled, sizeof(enabled) };
        SetLastError(0);
        if (!setComposition(hwnd, &exclusion))
        {
            DWORD code = GetLastError();
            return fail(HRESULT_FROM_WIN32(code ? code : ERROR_NOT_SUPPORTED), L"Own-window Desktop Duplication exclusion failed");
        }
        excluded = true;

        ComPtr<IDXGIFactory1> factory;
        HRESULT hr = CreateDXGIFactory1(IID_PPV_ARGS(&factory));
        if (FAILED(hr)) return fail(hr, L"CreateDXGIFactory1");
        HMONITOR monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        for (UINT i = 0; !output; i++)
        {
            ComPtr<IDXGIAdapter1> candidate;
            hr = factory->EnumAdapters1(i, &candidate);
            if (hr == DXGI_ERROR_NOT_FOUND) break;
            if (FAILED(hr)) return fail(hr, L"EnumAdapters1");
            for (UINT j = 0; ; j++)
            {
                ComPtr<IDXGIOutput> candidateOutput;
                hr = candidate->EnumOutputs(j, &candidateOutput);
                if (hr == DXGI_ERROR_NOT_FOUND) break;
                if (FAILED(hr)) return fail(hr, L"EnumOutputs");
                DXGI_OUTPUT_DESC description{};
                hr = candidateOutput->GetDesc(&description);
                if (FAILED(hr)) return fail(hr, L"Output GetDesc");
                if (description.Monitor == monitor)
                {
                    hr = candidateOutput.As(&output);
                    if (FAILED(hr)) return fail(hr, L"IDXGIOutput1");
                    adapter = candidate;
                    outputDesc = description;
                    break;
                }
            }
        }
        if (!output) return fail(DXGI_ERROR_NOT_FOUND, L"No DXGI output for this window's monitor");
        D3D_FEATURE_LEVEL supported{};
        D3D_FEATURE_LEVEL levels[] = { D3D_FEATURE_LEVEL_11_0, D3D_FEATURE_LEVEL_10_1, D3D_FEATURE_LEVEL_10_0 };
        hr = D3D11CreateDevice(adapter.Get(), D3D_DRIVER_TYPE_UNKNOWN, nullptr,
            D3D11_CREATE_DEVICE_BGRA_SUPPORT, levels, ARRAYSIZE(levels), D3D11_SDK_VERSION,
            &device, &supported, &immediate);
        if (FAILED(hr)) return fail(hr, L"D3D11CreateDevice on output adapter");
        hr = output->DuplicateOutput(device.Get(), &duplication);
        if (FAILED(hr)) return fail(hr, L"DuplicateOutput");
        duplication->GetDesc(&duplicationDesc);
        if (duplicationDesc.Rotation != DXGI_MODE_ROTATION_IDENTITY)
            return fail(DXGI_ERROR_UNSUPPORTED, L"Rotated output is not supported by this initial GPU bridge");
        if (duplicationDesc.ModeDesc.Format != DXGI_FORMAT_B8G8R8A8_UNORM)
            return fail(DXGI_ERROR_UNSUPPORTED, L"Unexpected desktop duplication pixel format");

        hr = Direct3DCreate9Ex(D3D_SDK_VERSION, &api9);
        if (FAILED(hr)) return fail(hr, L"Direct3DCreate9Ex");
        DXGI_ADAPTER_DESC1 adapterDesc{};
        hr = adapter->GetDesc1(&adapterDesc);
        if (FAILED(hr)) return fail(hr, L"Adapter GetDesc1");
        UINT adapter9 = UINT_MAX;
        for (UINT i = 0; i < api9->GetAdapterCount(); i++)
        {
            LUID luid{};
            if (SUCCEEDED(api9->GetAdapterLUID(i, &luid)) &&
                luid.HighPart == adapterDesc.AdapterLuid.HighPart && luid.LowPart == adapterDesc.AdapterLuid.LowPart)
            { adapter9 = i; break; }
        }
        if (adapter9 == UINT_MAX) return fail(DXGI_ERROR_UNSUPPORTED, L"No matching D3D9Ex adapter LUID");
        D3DPRESENT_PARAMETERS present{};
        present.Windowed = TRUE;
        present.SwapEffect = D3DSWAPEFFECT_DISCARD;
        present.hDeviceWindow = hwnd;
        present.BackBufferWidth = present.BackBufferHeight = 1;
        present.BackBufferFormat = D3DFMT_UNKNOWN;
        present.PresentationInterval = D3DPRESENT_INTERVAL_IMMEDIATE;
        hr = api9->CreateDeviceEx(adapter9, D3DDEVTYPE_HAL, hwnd,
            D3DCREATE_HARDWARE_VERTEXPROCESSING | D3DCREATE_MULTITHREADED | D3DCREATE_FPU_PRESERVE,
            &present, nullptr, &device9);
        if (FAILED(hr)) return fail(hr, L"D3D9Ex CreateDeviceEx on matching adapter");

        ComPtr<ID3DBlob> code;
        hr = compile("VS", "vs_4_0", &code);
        if (FAILED(hr)) return hr;
        hr = device->CreateVertexShader(code->GetBufferPointer(), code->GetBufferSize(), nullptr, &vertex);
        if (FAILED(hr)) return fail(hr, L"CreateVertexShader");
        hr = compile("PSDownsample", "ps_4_0", &code);
        if (FAILED(hr)) return hr;
        hr = device->CreatePixelShader(code->GetBufferPointer(), code->GetBufferSize(), nullptr, &downsampleShader);
        if (FAILED(hr)) return fail(hr, L"Create anti-alias downsample pixel shader");
        hr = compile("PSBlur", "ps_4_0", &code);
        if (FAILED(hr)) return hr;
        hr = device->CreatePixelShader(code->GetBufferPointer(), code->GetBufferSize(), nullptr, &blurShader);
        if (FAILED(hr)) return fail(hr, L"Create Gaussian pixel shader");
        hr = compile("PSCompose", "ps_4_0", &code);
        if (FAILED(hr)) return hr;
        hr = device->CreatePixelShader(code->GetBufferPointer(), code->GetBufferSize(), nullptr, &composeShader);
        if (FAILED(hr)) return fail(hr, L"Create optical pixel shader");

        D3D11_BUFFER_DESC cb{};
        cb.ByteWidth = sizeof(Constants);
        cb.Usage = D3D11_USAGE_DYNAMIC;
        cb.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
        cb.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
        hr = device->CreateBuffer(&cb, nullptr, &constants);
        if (FAILED(hr)) return fail(hr, L"Create constant buffer");
        D3D11_SAMPLER_DESC sample{};
        sample.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
        sample.AddressU = sample.AddressV = sample.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
        sample.MaxLOD = D3D11_FLOAT32_MAX;
        hr = device->CreateSamplerState(&sample, &sampler);
        if (FAILED(hr)) return fail(hr, L"Create sampler");
        D3D11_RASTERIZER_DESC raster{};
        raster.FillMode = D3D11_FILL_SOLID;
        raster.CullMode = D3D11_CULL_NONE;
        raster.DepthClipEnable = TRUE;
        hr = device->CreateRasterizerState(&raster, &rasterizer);
        if (FAILED(hr)) return fail(hr, L"Create rasterizer");
        D3D11_QUERY_DESC query{};
        query.Query = D3D11_QUERY_EVENT;
        hr = device->CreateQuery(&query, &completed);
        if (FAILED(hr)) return fail(hr, L"Create bounded GPU completion query");
        hr = compile("PSCompare", "ps_4_0", &code);
        if (FAILED(hr)) return hr;
        hr = device->CreatePixelShader(code->GetBufferPointer(), code->GetBufferSize(), nullptr, &compareShader);
        if (FAILED(hr)) return fail(hr, L"Create exact background change shader");
        query.Query = D3D11_QUERY_OCCLUSION_PREDICATE;
        hr = device->CreatePredicate(&query, &differentPixels);
        if (FAILED(hr)) return fail(hr, L"Create background difference predicate");
        D3D11_BLEND_DESC noWrites{};
        hr = device->CreateBlendState(&noWrites, &noColorWrites);
        if (FAILED(hr)) return fail(hr, L"Create non-writing comparison state");
        error = L"Ready: own-HWND excluded from DDA; matching D3D11/D3D9Ex adapter.";
        return S_OK;
    }

    HRESULT compile(const char* entry, const char* profile, ID3DBlob** result)
    {
        // Five immutable bytecode blobs per process, never device objects or frame data.
        static std::mutex mutex;
        static std::map<std::string, ComPtr<ID3DBlob>> cache;
        std::lock_guard<std::mutex> lock(mutex);
        std::string key=std::string(entry)+"/"+profile;
        auto found=cache.find(key);
        if (found!=cache.end()) { *result=found->second.Get(); (*result)->AddRef(); return S_OK; }
        ComPtr<ID3DBlob> diagnostics;
        HRESULT hr = D3DCompile(std::strcmp(entry, "PSCompare") == 0 ? kComparisonSource : kShaderSource,
            std::strlen(std::strcmp(entry, "PSCompare") == 0 ? kComparisonSource : kShaderSource), "LiveGlass-original.hlsl",
            nullptr, nullptr, entry, profile,
            D3DCOMPILE_ENABLE_STRICTNESS | D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, result, &diagnostics);
        if (FAILED(hr))
        {
            fail(hr, L"Compile original live glass shader");
            if (diagnostics)
            {
                const char* message = static_cast<const char*>(diagnostics->GetBufferPointer());
                int size = MultiByteToWideChar(CP_UTF8, 0, message, -1, nullptr, 0);
                if (size > 1)
                {
                    std::wstring details(size, L'\0');
                    MultiByteToWideChar(CP_UTF8, 0, message, -1, details.data(), size);
                    details.resize(size - 1);
                    error += L": " + details;
                    globalError = error;
                }
            }
        }
        if (SUCCEEDED(hr)) cache[key]=*result;
        return hr;
    }

    HRESULT makeTarget(UINT w, UINT h, Target& target)
    {
        D3D11_TEXTURE2D_DESC desc{};
        desc.Width = w; desc.Height = h;
        desc.MipLevels = desc.ArraySize = 1;
        desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        desc.SampleDesc.Count = 1;
        desc.Usage = D3D11_USAGE_DEFAULT;
        desc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
        HRESULT hr = device->CreateTexture2D(&desc, nullptr, &target.texture);
        if (FAILED(hr)) return fail(hr, L"Create Gaussian render target");
        hr = device->CreateRenderTargetView(target.texture.Get(), nullptr, &target.rtv);
        if (FAILED(hr)) return fail(hr, L"Gaussian render-target view");
        hr = device->CreateShaderResourceView(target.texture.Get(), nullptr, &target.srv);
        return FAILED(hr) ? fail(hr, L"Gaussian shader-resource view") : S_OK;
    }

    HRESULT resize(int w, int h)
    {
        if (w < 1 || h < 1 || w > 8192 || h > 8192) return fail(E_INVALIDARG, L"Invalid render-target size");
        if (w == width && h == height && surface9) { parametersDirty = true; return S_OK; }
        immediate->ClearState(); immediate->Flush();
        ComPtr<IDirect3DTexture9> next9;
        ComPtr<IDirect3DSurface9> nextSurface;
        ComPtr<ID3D11Texture2D> next11;
        ComPtr<ID3D11RenderTargetView> nextRtv;
        HANDLE shared = nullptr;
        // This is the small public shared-surface pattern used by Microsoft's
        // WPF DirectX interop sample. HRESULT/format are checked on the host.
        HRESULT hr = device9->CreateTexture(w, h, 1, D3DUSAGE_RENDERTARGET,
            D3DFMT_A8R8G8B8, D3DPOOL_DEFAULT, &next9, &shared);
        if (FAILED(hr)) return fail(hr, L"D3D9Ex CreateTexture shared BGRA");
        hr = next9->GetSurfaceLevel(0, &nextSurface);
        if (FAILED(hr)) return fail(hr, L"D3D9Ex GetSurfaceLevel");
        hr = device->OpenSharedResource(shared, IID_PPV_ARGS(&next11));
        if (FAILED(hr)) return fail(hr, L"D3D11 OpenSharedResource BGRA");
        D3D11_TEXTURE2D_DESC actual{};
        next11->GetDesc(&actual);
        D3DSURFACE_DESC actual9{};
        hr = nextSurface->GetDesc(&actual9);
        if (FAILED(hr)) return fail(hr, L"D3D9 shared-surface GetDesc");
        if (actual.Format != DXGI_FORMAT_B8G8R8A8_UNORM || actual9.Format != D3DFMT_A8R8G8B8 ||
            actual.Width != static_cast<UINT>(w) || actual.Height != static_cast<UINT>(h) ||
            actual.SampleDesc.Count != 1)
            return fail(DXGI_ERROR_UNSUPPORTED, L"Shared surface is not compatible single-sample BGRA");
        hr = device->CreateRenderTargetView(next11.Get(), nullptr, &nextRtv);
        if (FAILED(hr)) return fail(hr, L"Shared BGRA render-target view");
        UINT qw = (w + 3) / 4, qh = (h + 3) / 4;
        Target nextPrefiltered, nextIntermediate, nextSoftened;
        hr = makeTarget(qw, qh, nextPrefiltered); if (FAILED(hr)) return hr;
        hr = makeTarget(qw, qh, nextIntermediate); if (FAILED(hr)) return hr;
        hr = makeTarget(qw, qh, nextSoftened); if (FAILED(hr)) return hr;
        texture9 = std::move(next9); surface9 = std::move(nextSurface);
        shared11 = std::move(next11); sharedRtv = std::move(nextRtv);
        prefiltered = std::move(nextPrefiltered);
        intermediate = std::move(nextIntermediate);
        softened = std::move(nextSoftened);
        hr = makeTarget(w + 16, h + 16, comparisonTarget); if (FAILED(hr)) return hr;
        width = w; height = h;
        pending = false; parametersDirty = true;
        error = L"Ready: shared surface verified B8G8R8A8_UNORM / A8R8G8B8; single sample.";
        return S_OK;
    }

    bool affectsRegion(const DXGI_OUTDUPL_FRAME_INFO& frame, const RECT& roi)
    {
        // Metadata is conservative: on failure/empty metadata compare actual pixels.
        if (frame.TotalMetadataBufferSize == 0) return true;
        metadata.resize(frame.TotalMetadataBufferSize);
        UINT count = 0;
        HRESULT hr = duplication->GetFrameMoveRects(static_cast<UINT>(metadata.size()),
            reinterpret_cast<DXGI_OUTDUPL_MOVE_RECT*>(metadata.data()), &count);
        if (FAILED(hr)) return true;
        auto intersects = [&](const RECT& r) { RECT result{}; return IntersectRect(&result, &r, &roi) != FALSE; };
        auto moves = reinterpret_cast<const DXGI_OUTDUPL_MOVE_RECT*>(metadata.data());
        for (UINT i = 0; i < count / sizeof(*moves); i++)
        {
            if (intersects(moves[i].DestinationRect)) return true;
            RECT source{ moves[i].SourcePoint.x, moves[i].SourcePoint.y,
                moves[i].SourcePoint.x + moves[i].DestinationRect.right - moves[i].DestinationRect.left,
                moves[i].SourcePoint.y + moves[i].DestinationRect.bottom - moves[i].DestinationRect.top };
            if (intersects(source)) return true;
        }
        hr = duplication->GetFrameDirtyRects(static_cast<UINT>(metadata.size()),
            reinterpret_cast<RECT*>(metadata.data()), &count);
        if (FAILED(hr)) return true;
        auto rects = reinterpret_cast<const RECT*>(metadata.data());
        for (UINT i = 0; i < count / sizeof(*rects); i++) if (intersects(rects[i])) return true;
        return false;
    }

    HRESULT capture(bool& changed, const RECT& roi)
    {
        changed = false;
        DXGI_OUTDUPL_FRAME_INFO frame{};
        ComPtr<IDXGIResource> resource;
        HRESULT hr = duplication->AcquireNextFrame(0, &frame, &resource);
        if (hr == DXGI_ERROR_WAIT_TIMEOUT) return S_OK;
        if (FAILED(hr)) return fail(hr, L"AcquireNextFrame (device/access loss requires safe recreation)");
        frameHeld = true;
        ComPtr<ID3D11Texture2D> captured;
        hr = resource.As(&captured);
        if (SUCCEEDED(hr) && !latest)
        {
            D3D11_TEXTURE2D_DESC desc{};
            captured->GetDesc(&desc);
            desc.Usage = D3D11_USAGE_DEFAULT;
            desc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
            desc.CPUAccessFlags = 0; desc.MiscFlags = 0;
            hr = device->CreateTexture2D(&desc, nullptr, &latest);
            if (SUCCEEDED(hr)) hr = device->CreateShaderResourceView(latest.Get(), nullptr, &latestSrv);
            if (SUCCEEDED(hr)) hr = device->CreateTexture2D(&desc, nullptr, &previous);
            if (SUCCEEDED(hr)) hr = device->CreateShaderResourceView(previous.Get(), nullptr, &previousSrv);
        }
        if (SUCCEEDED(hr) && (!hasLatest || frame.LastPresentTime.QuadPart != 0))
        {
            changed = !hasLatest || affectsRegion(frame, roi);
            if (!changed) outsideSkips++;
            std::swap(latest, previous); std::swap(latestSrv, previousSrv);
            immediate->CopyResource(latest.Get(), captured.Get());
            hasLatest = true; captures++;
        }
        HRESULT release = duplication->ReleaseFrame();
        frameHeld = false;
        if (FAILED(hr)) return fail(hr, L"Copy desktop frame into owned GPU texture");
        return FAILED(release) ? fail(release, L"ReleaseFrame") : S_OK;
    }

    HRESULT pass(ID3D11ShaderResourceView* source, ID3D11ShaderResourceView* second,
                 ID3D11RenderTargetView* destination, ID3D11PixelShader* shader, const Constants& cb)
    {
        ID3D11ShaderResourceView* none[2] = {};
        immediate->PSSetShaderResources(0, 2, none);
        D3D11_MAPPED_SUBRESOURCE mapped{};
        HRESULT hr = immediate->Map(constants.Get(), 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped);
        if (FAILED(hr)) return fail(hr, L"Update shader constants");
        std::memcpy(mapped.pData, &cb, sizeof(cb));
        immediate->Unmap(constants.Get(), 0);
        immediate->OMSetRenderTargets(1, &destination, nullptr);
        ID3D11ShaderResourceView* resources[] = { source, second };
        immediate->PSSetShaderResources(0, 2, resources);
        immediate->PSSetShader(shader, nullptr, 0);
        immediate->Draw(3, 0);
        return S_OK;
    }

    int render(int screenX, int screenY, int w, int h, float density, bool dark)
    {
        if (w != width || h != height || !surface9)
            return fail(E_INVALIDARG, L"Resize and rebind D3DImage before rendering a new size");
        if (MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST) != outputDesc.Monitor)
            return fail(DXGI_ERROR_ACCESS_LOST, L"Window moved to another monitor; recreate on its adapter");
        if (!std::isfinite(density)) return fail(E_INVALIDARG, L"Non-finite material density");
        density = std::clamp(density, 0.0f, 1.0f);
        bool changedParameters = parametersDirty || screenX != lastX || screenY != lastY ||
            dark != (lastDark == 1) || density != lastDensity;
        // No spin wait on the WPF UI thread. A completed pending frame is
        // announced on a later tick, including when the desktop is static.
        if (pending)
        {
            BOOL ready = FALSE;
            HRESULT status = immediate->GetData(completed.Get(), &ready, sizeof(ready), D3D11_ASYNC_GETDATA_DONOTFLUSH);
            if (FAILED(status)) return fail(status, L"GPU event completion / device loss");
            if (status == S_FALSE || !ready)
            {
                if (GetTickCount64() - pendingAt > 300) return fail(DXGI_ERROR_DEVICE_HUNG, L"GPU event exceeded 300ms; safe fallback required");
                return 0;
            }
            if (conditionalPending)
            {
                BOOL changed = FALSE;
                status = immediate->GetData(differentPixels.Get(), &changed, sizeof(changed), D3D11_ASYNC_GETDATA_DONOTFLUSH);
                if (FAILED(status)) return fail(status, L"Background difference query / device loss");
                if (status == S_FALSE) {
                    if (GetTickCount64() - pendingAt > 300) return fail(DXGI_ERROR_DEVICE_HUNG, L"Difference query exceeded 300ms");
                    return 0;
                }
                pending = false; conditionalPending = false;
                if (!changed) { equalSkips++; return 0; }
            }
            pending = false; draws++;
            return 1;
        }
        bool captured = false;
        RECT roi{ screenX - outputDesc.DesktopCoordinates.left - 8,
            screenY - outputDesc.DesktopCoordinates.top - 8,
            screenX - outputDesc.DesktopCoordinates.left + w + 8,
            screenY - outputDesc.DesktopCoordinates.top + h + 8 };
        HRESULT hr = capture(captured, roi);
        if (FAILED(hr)) return hr;
        if (!hasLatest || (!captured && !changedParameters)) return 0;

        Constants cb{};
        cb.frame[0] = static_cast<float>(duplicationDesc.ModeDesc.Width);
        cb.frame[1] = static_cast<float>(duplicationDesc.ModeDesc.Height);
        cb.frame[2] = static_cast<float>(w); cb.frame[3] = static_cast<float>(h);
        cb.crop[0] = static_cast<float>(screenX - outputDesc.DesktopCoordinates.left);
        cb.crop[1] = static_cast<float>(screenY - outputDesc.DesktopCoordinates.top);
        cb.crop[2] = static_cast<float>(w); cb.crop[3] = static_cast<float>(h);
        cb.style[0] = density; cb.style[1] = dark ? 1.0f : 0.0f;

        D3D11_VIEWPORT viewport{};
        viewport.Width = static_cast<float>(w); viewport.Height = static_cast<float>(h);
        viewport.MaxDepth = 1;
        immediate->RSSetViewports(1, &viewport);
        immediate->RSSetState(rasterizer.Get());
        immediate->IASetInputLayout(nullptr);
        immediate->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        immediate->VSSetShader(vertex.Get(), nullptr, 0);
        ID3D11Buffer* buffer = constants.Get();
        immediate->PSSetConstantBuffers(0, 1, &buffer);
        ID3D11SamplerState* sample = sampler.Get();
        immediate->PSSetSamplers(0, 1, &sample);
        conditionalPending = captured && !changedParameters;
        if (conditionalPending)
        {
            // Pixel-exact RGB equality; no threshold, downsampling, or visual loss.
            // Probe writes neither the shared glass image nor any visible surface.
            viewport.Width = static_cast<float>(w + 16); viewport.Height = static_cast<float>(h + 16);
            immediate->RSSetViewports(1, &viewport);
            immediate->OMSetBlendState(noColorWrites.Get(), nullptr, 0xffffffff);
            immediate->Begin(differentPixels.Get());
            hr = pass(latestSrv.Get(), previousSrv.Get(), comparisonTarget.rtv.Get(), compareShader.Get(), cb);
            immediate->End(differentPixels.Get());
            immediate->OMSetBlendState(nullptr, nullptr, 0xffffffff);
            if (FAILED(hr)) return hr;
            // SetPredication suppresses commands when the result equals its value.
            // FALSE therefore keeps the original optical passes only for changed pixels.
            immediate->SetPredication(differentPixels.Get(), FALSE);
        }
        cb.frame[2] = static_cast<float>((w + 3) / 4);
        cb.frame[3] = static_cast<float>((h + 3) / 4);
        viewport.Width = cb.frame[2]; viewport.Height = cb.frame[3];
        immediate->RSSetViewports(1, &viewport);
        hr = pass(latestSrv.Get(), nullptr, prefiltered.rtv.Get(), downsampleShader.Get(), cb);
        if (FAILED(hr)) return hr;
        float scale = static_cast<float>(w) / 760.0f;
        // A single continuous softening field; no content-region protection.
        // Sigma is 4-12 full-resolution pixels at 100% DPI, then quarter-scaled.
        cb.blur[0] = 1; cb.blur[1] = 0;
        cb.blur[2] = (4.0f + 8.0f * density) * scale * 0.25f;
        hr = pass(prefiltered.srv.Get(), nullptr, intermediate.rtv.Get(), blurShader.Get(), cb);
        if (FAILED(hr)) return hr;
        cb.blur[0] = 0; cb.blur[1] = 1;
        hr = pass(intermediate.srv.Get(), nullptr, softened.rtv.Get(), blurShader.Get(), cb);
        if (FAILED(hr)) return hr;
        cb.frame[2] = static_cast<float>(w); cb.frame[3] = static_cast<float>(h);
        viewport.Width = cb.frame[2]; viewport.Height = cb.frame[3];
        immediate->RSSetViewports(1, &viewport);
        hr = pass(softened.srv.Get(), latestSrv.Get(), sharedRtv.Get(), composeShader.Get(), cb);
        if (FAILED(hr)) return hr;
        ID3D11ShaderResourceView* none[2] = {};
        immediate->PSSetShaderResources(0, 2, none);
        immediate->OMSetRenderTargets(0, nullptr, nullptr);
        immediate->SetPredication(nullptr, FALSE);
        immediate->End(completed.Get());
        immediate->Flush(); // submission only; GetData above proves completion
        lastX = screenX; lastY = screenY; lastDark = dark ? 1 : 0;
        lastDensity = density; parametersDirty = false;
        pending = true; pendingAt = GetTickCount64();
        return 0;
    }
};

#define LIVE_API extern "C" __declspec(dllexport)
LIVE_API void* __cdecl LiveGlass_Create(HWND window) noexcept
{
    try
    {
        auto context = std::make_unique<LiveGlass>();
        HRESULT hr = context->init(window);
        if (FAILED(hr)) return nullptr;
        return context.release();
    }
    catch (...) { globalError = L"Create: unexpected native exception."; return nullptr; }
}
LIVE_API int __cdecl LiveGlass_Resize(void* handle, int width, int height) noexcept
{
    if (!handle) return E_POINTER;
    try { HRESULT hr = static_cast<LiveGlass*>(handle)->resize(width, height); return FAILED(hr) ? hr : 1; }
    catch (...) { return static_cast<LiveGlass*>(handle)->fail(E_FAIL, L"Resize: unexpected native exception"); }
}
LIVE_API int __cdecl LiveGlass_Render(void* handle, int screenX, int screenY, int width, int height, float density, int dark) noexcept
{
    if (!handle) return E_POINTER;
    try { return static_cast<LiveGlass*>(handle)->render(screenX, screenY, width, height, density, dark != 0); }
    catch (...) { return static_cast<LiveGlass*>(handle)->fail(E_FAIL, L"Render: unexpected native exception"); }
}
LIVE_API IDirect3DSurface9* __cdecl LiveGlass_Surface9(void* handle) noexcept
{
    if (!handle) return nullptr;
    auto surface = static_cast<LiveGlass*>(handle)->surface9.Get();
    if (surface) surface->AddRef();
    return surface;
}
LIVE_API void __cdecl LiveGlass_SetContentRect(void*, float, float, float, float) noexcept
{
    // Retained for existing bridge ABI compatibility. The user-requested clear
    // material intentionally has no content-region mask or special blur zone.
}
LIVE_API int __cdecl LiveGlass_ContextCount() noexcept { return liveContexts.load(); }
LIVE_API void __cdecl LiveGlass_Invalidate(void* handle) noexcept
{
    if (handle) static_cast<LiveGlass*>(handle)->parametersDirty = true;
}
LIVE_API const wchar_t* __cdecl LiveGlass_Stats(void* handle) noexcept
{
    static thread_local std::wstring text;
    if (!handle) return L"no-context";
    auto c = static_cast<LiveGlass*>(handle);
    std::wostringstream s;
    s << L"captures=" << c->captures << L", outside=" << c->outsideSkips
      << L", equal=" << c->equalSkips << L", opticalFrames=" << c->draws;
    text = s.str(); return text.c_str();
}
LIVE_API int __cdecl LiveGlass_IsPending(void* handle) noexcept
{
    return handle && static_cast<LiveGlass*>(handle)->pending ? 1 : 0;
}
LIVE_API const wchar_t* __cdecl LiveGlass_LastError(void* handle) noexcept
{
    return handle ? static_cast<LiveGlass*>(handle)->error.c_str() : globalError.c_str();
}
LIVE_API void __cdecl LiveGlass_Destroy(void* handle) noexcept
{
    delete static_cast<LiveGlass*>(handle);
}


