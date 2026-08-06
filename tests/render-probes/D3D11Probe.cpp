#include "ProbeWindow.h"

#include <d3d11.h>
#include <dxgi.h>
#include <wrl/client.h>
#include <cmath>

using Microsoft::WRL::ComPtr;

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    HWND window = CreateProbeWindow(L"GameNest DirectX 11 Render Probe");
    if (window == nullptr)
    {
        return 1;
    }

    DXGI_SWAP_CHAIN_DESC swap_desc{};
    UINT client_width = 0;
    UINT client_height = 0;
    GetProbeClientSize(window, client_width, client_height);
    swap_desc.BufferCount = 2;
    swap_desc.BufferDesc.Width = client_width;
    swap_desc.BufferDesc.Height = client_height;
    swap_desc.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    swap_desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    swap_desc.OutputWindow = window;
    swap_desc.SampleDesc.Count = 1;
    swap_desc.Windowed = TRUE;
    swap_desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;

    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    ComPtr<IDXGISwapChain> swap_chain;
    if (FAILED(D3D11CreateDeviceAndSwapChain(
            nullptr,
            D3D_DRIVER_TYPE_HARDWARE,
            nullptr,
            0,
            nullptr,
            0,
            D3D11_SDK_VERSION,
            &swap_desc,
            &swap_chain,
            &device,
            nullptr,
            &context)))
    {
        return 2;
    }

    ComPtr<ID3D11Texture2D> back_buffer;
    ComPtr<ID3D11RenderTargetView> render_target;
    if (FAILED(swap_chain->GetBuffer(0, IID_PPV_ARGS(&back_buffer))) ||
        FAILED(device->CreateRenderTargetView(back_buffer.Get(), nullptr, &render_target)))
    {
        return 3;
    }

    const ULONGLONG started = GetTickCount64();
    while (PumpProbeMessages())
    {
        const float phase = static_cast<float>((GetTickCount64() - started) % 5000) / 5000.0f;
        const float color[] = {
            0.08f + 0.12f * std::sin(phase * 6.28318f),
            0.24f,
            0.46f + 0.12f * std::cos(phase * 6.28318f),
            1.0f,
        };
        context->ClearRenderTargetView(render_target.Get(), color);
        swap_chain->Present(1, 0);
    }

    return 0;
}
