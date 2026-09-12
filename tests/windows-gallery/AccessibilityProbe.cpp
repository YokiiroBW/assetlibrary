#include <windows.h>
#include <oleacc.h>
#include <UIAutomation.h>
#include <iostream>
#include <cwchar>

namespace { void Require(bool value, const char* message) { if (!value) throw message; } }
int wmain(int argc, wchar_t** argv) {
    if (argc != 2) return 2;
    const HWND window = reinterpret_cast<HWND>(_wcstoui64(argv[1], nullptr, 10));
    if (!window || !IsWindow(window) || FAILED(CoInitializeEx(nullptr, COINIT_MULTITHREADED))) return 2;
    IAccessible* accessible = nullptr; IUIAutomation* automation = nullptr; IUIAutomationElement* element = nullptr;
    IUIAutomationCondition* condition = nullptr; IUIAutomationElementArray* children = nullptr;
    int stage = 10;
    try {
        Require(SUCCEEDED(AccessibleObjectFromWindow(window, static_cast<DWORD>(OBJID_CLIENT), IID_IAccessible,
            reinterpret_cast<void**>(&accessible))), "external MSAA retrieval");
        LONG count = 0; Require(SUCCEEDED(accessible->get_accChildCount(&count)) && count == 30, "external MSAA children");
        VARIANT child{}; child.vt = VT_I4; child.lVal = 3;
        BSTR name = nullptr; Require(SUCCEEDED(accessible->get_accName(child, &name)) && name && SysStringLen(name) > 10, "external full name"); SysFreeString(name);
        Require(SUCCEEDED(accessible->accSelect(SELFLAG_TAKESELECTION | SELFLAG_TAKEFOCUS, child)), "external MSAA selection");
        VARIANT selection{}; Require(SUCCEEDED(accessible->get_accSelection(&selection)) && selection.vt == VT_I4 && selection.lVal == 3, "external selection readback"); VariantClear(&selection);
        child.lVal = 1; Require(SUCCEEDED(accessible->accDoDefaultAction(child)), "external directory action");
        stage = 11;
        Require(SUCCEEDED(CoCreateInstance(CLSID_CUIAutomation, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&automation))), "UIA client creation");
        stage = 12;
        Require(SUCCEEDED(automation->ElementFromHandle(window, &element)), "UIA bridge element");
        stage = 13;
        CONTROLTYPEID type = 0; Require(SUCCEEDED(element->get_CurrentControlType(&type)) && type == UIA_ListControlTypeId, "UIA bridge List role");
        stage = 14;
        VARIANT itemType{}; itemType.vt = VT_I4; itemType.lVal = UIA_ListItemControlTypeId;
        Require(SUCCEEDED(automation->CreatePropertyCondition(UIA_ControlTypePropertyId, itemType, &condition)), "UIA list-item condition");
        stage = 15;
        Require(SUCCEEDED(element->FindAll(TreeScope_Children, condition, &children)), "UIA child enumeration");
        int length = 0; Require(SUCCEEDED(children->get_Length(&length)), "UIA child count query");
        stage = 100 + length; Require(length == 30, "UIA bridge exposes all current-page items");
        children->Release(); condition->Release(); element->Release(); automation->Release(); accessible->Release();
        CoUninitialize(); std::cout << "external MSAA/UIA bridge, selection and default action passed\n"; return 0;
    } catch (const char* error) {
        std::cerr << error << '\n'; if (children) children->Release(); if (condition) condition->Release();
        if (element) element->Release(); if (automation) automation->Release(); if (accessible) accessible->Release(); CoUninitialize(); return stage;
    }
}
