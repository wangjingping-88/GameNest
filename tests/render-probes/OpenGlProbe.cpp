#include "ProbeWindow.h"

#include <gl/GL.h>
#include <cmath>

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    HWND window = CreateProbeWindow(L"GameNest OpenGL Render Probe");
    if (window == nullptr)
    {
        return 1;
    }

    HDC device_context = GetDC(window);
    PIXELFORMATDESCRIPTOR format{};
    format.nSize = sizeof(format);
    format.nVersion = 1;
    format.dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER;
    format.iPixelType = PFD_TYPE_RGBA;
    format.cColorBits = 32;
    format.cDepthBits = 24;
    const int format_index = ChoosePixelFormat(device_context, &format);
    if (format_index == 0 || !SetPixelFormat(device_context, format_index, &format))
    {
        return 2;
    }

    HGLRC rendering_context = wglCreateContext(device_context);
    if (rendering_context == nullptr || !wglMakeCurrent(device_context, rendering_context))
    {
        return 3;
    }

    const ULONGLONG started = GetTickCount64();
    while (PumpProbeMessages())
    {
        UINT client_width = 0;
        UINT client_height = 0;
        GetProbeClientSize(window, client_width, client_height);
        const float phase = static_cast<float>((GetTickCount64() - started) % 5000) / 5000.0f;
        glViewport(0, 0, static_cast<GLsizei>(client_width), static_cast<GLsizei>(client_height));
        glClearColor(
            0.12f,
            0.28f + 0.12f * std::sin(phase * 6.28318f),
            0.42f + 0.12f * std::cos(phase * 6.28318f),
            1.0f);
        glClear(GL_COLOR_BUFFER_BIT);
        SwapBuffers(device_context);
        Sleep(16);
    }

    wglMakeCurrent(nullptr, nullptr);
    wglDeleteContext(rendering_context);
    ReleaseDC(window, device_context);
    return 0;
}
