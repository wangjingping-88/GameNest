#include "ProbeWindow.h"

#include <d3d12.h>
#include <dxgi1_6.h>
#include <wrl/client.h>
#include <cmath>

using Microsoft::WRL::ComPtr;

namespace
{
constexpr UINT FrameCount = 2;

D3D12_RESOURCE_BARRIER TransitionBarrier(
    ID3D12Resource* resource,
    D3D12_RESOURCE_STATES before,
    D3D12_RESOURCE_STATES after)
{
    D3D12_RESOURCE_BARRIER barrier{};
    barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    barrier.Transition.pResource = resource;
    barrier.Transition.StateBefore = before;
    barrier.Transition.StateAfter = after;
    barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    return barrier;
}
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    HWND window = CreateProbeWindow(L"GameNest DirectX 12 Render Probe");
    if (window == nullptr)
    {
        return 1;
    }

    ComPtr<IDXGIFactory6> factory;
    ComPtr<ID3D12Device> device;
    if (FAILED(CreateDXGIFactory2(0, IID_PPV_ARGS(&factory))) ||
        FAILED(D3D12CreateDevice(nullptr, D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&device))))
    {
        return 2;
    }

    D3D12_COMMAND_QUEUE_DESC queue_desc{};
    queue_desc.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
    ComPtr<ID3D12CommandQueue> queue;
    if (FAILED(device->CreateCommandQueue(&queue_desc, IID_PPV_ARGS(&queue))))
    {
        return 3;
    }

    DXGI_SWAP_CHAIN_DESC1 swap_desc{};
    GetProbeClientSize(window, swap_desc.Width, swap_desc.Height);
    swap_desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    swap_desc.BufferCount = FrameCount;
    swap_desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    swap_desc.SampleDesc.Count = 1;
    swap_desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
    ComPtr<IDXGISwapChain1> swap_chain_base;
    ComPtr<IDXGISwapChain3> swap_chain;
    if (FAILED(factory->CreateSwapChainForHwnd(
            queue.Get(),
            window,
            &swap_desc,
            nullptr,
            nullptr,
            &swap_chain_base)) ||
        FAILED(swap_chain_base.As(&swap_chain)))
    {
        return 4;
    }

    D3D12_DESCRIPTOR_HEAP_DESC heap_desc{};
    heap_desc.NumDescriptors = FrameCount;
    heap_desc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_RTV;
    ComPtr<ID3D12DescriptorHeap> rtv_heap;
    if (FAILED(device->CreateDescriptorHeap(&heap_desc, IID_PPV_ARGS(&rtv_heap))))
    {
        return 5;
    }

    const UINT descriptor_size = device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
    ComPtr<ID3D12Resource> render_targets[FrameCount];
    ComPtr<ID3D12CommandAllocator> allocators[FrameCount];
    D3D12_CPU_DESCRIPTOR_HANDLE handle = rtv_heap->GetCPUDescriptorHandleForHeapStart();
    for (UINT index = 0; index < FrameCount; ++index)
    {
        if (FAILED(swap_chain->GetBuffer(index, IID_PPV_ARGS(&render_targets[index]))) ||
            FAILED(device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&allocators[index]))))
        {
            return 6;
        }

        device->CreateRenderTargetView(render_targets[index].Get(), nullptr, handle);
        handle.ptr += descriptor_size;
    }

    ComPtr<ID3D12GraphicsCommandList> command_list;
    if (FAILED(device->CreateCommandList(
            0,
            D3D12_COMMAND_LIST_TYPE_DIRECT,
            allocators[0].Get(),
            nullptr,
            IID_PPV_ARGS(&command_list))) ||
        FAILED(command_list->Close()))
    {
        return 7;
    }

    ComPtr<ID3D12Fence> fence;
    if (FAILED(device->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&fence))))
    {
        return 8;
    }

    HANDLE fence_event = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    if (fence_event == nullptr)
    {
        return 9;
    }

    UINT64 fence_value = 0;
    const ULONGLONG started = GetTickCount64();
    while (PumpProbeMessages())
    {
        const UINT frame_index = swap_chain->GetCurrentBackBufferIndex();
        allocators[frame_index]->Reset();
        command_list->Reset(allocators[frame_index].Get(), nullptr);
        auto to_render = TransitionBarrier(
            render_targets[frame_index].Get(),
            D3D12_RESOURCE_STATE_PRESENT,
            D3D12_RESOURCE_STATE_RENDER_TARGET);
        command_list->ResourceBarrier(1, &to_render);
        auto target = rtv_heap->GetCPUDescriptorHandleForHeapStart();
        target.ptr += static_cast<SIZE_T>(frame_index) * descriptor_size;
        command_list->OMSetRenderTargets(1, &target, FALSE, nullptr);
        const float phase = static_cast<float>((GetTickCount64() - started) % 5000) / 5000.0f;
        const float color[] = {
            0.20f,
            0.18f + 0.12f * std::sin(phase * 6.28318f),
            0.55f + 0.12f * std::cos(phase * 6.28318f),
            1.0f,
        };
        command_list->ClearRenderTargetView(target, color, 0, nullptr);
        auto to_present = TransitionBarrier(
            render_targets[frame_index].Get(),
            D3D12_RESOURCE_STATE_RENDER_TARGET,
            D3D12_RESOURCE_STATE_PRESENT);
        command_list->ResourceBarrier(1, &to_present);
        command_list->Close();
        ID3D12CommandList* lists[] = {command_list.Get()};
        queue->ExecuteCommandLists(1, lists);
        swap_chain->Present(1, 0);

        ++fence_value;
        queue->Signal(fence.Get(), fence_value);
        if (fence->GetCompletedValue() < fence_value)
        {
            fence->SetEventOnCompletion(fence_value, fence_event);
            WaitForSingleObject(fence_event, INFINITE);
        }
    }

    CloseHandle(fence_event);
    return 0;
}
