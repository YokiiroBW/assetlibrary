#include <windows.h>
#include <oleacc.h>
#include <UIAutomation.h>
#include <iostream>
#include <cwchar>
#include <atomic>

namespace {
void Require(bool value, const char* message) { if (!value) throw message; }
class Listener final : public IUIAutomationEventHandler, public IUIAutomationFocusChangedEventHandler {
    std::atomic_ulong references_{1};
public:
    std::atomic_uint selections{0}, focuses{0}, focusCalls{0}; std::atomic_int lastProcess{0}; DWORD process = 0;
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** result) override {
        if (!result) return E_POINTER; *result = nullptr;
        if (iid == IID_IUnknown || iid == __uuidof(IUIAutomationEventHandler)) *result = static_cast<IUIAutomationEventHandler*>(this);
        else if (iid == __uuidof(IUIAutomationFocusChangedEventHandler)) *result = static_cast<IUIAutomationFocusChangedEventHandler*>(this);
        else return E_NOINTERFACE; AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++references_; }
    ULONG STDMETHODCALLTYPE Release() override { const auto count = --references_; if (!count) delete this; return count; }
    HRESULT STDMETHODCALLTYPE HandleAutomationEvent(IUIAutomationElement*, EVENTID event) override { if (event == UIA_Selection_InvalidatedEventId) ++selections; return S_OK; }
    HRESULT STDMETHODCALLTYPE HandleFocusChangedEvent(IUIAutomationElement* sender) override {
        ++focusCalls; int senderProcess = 0; if (sender && SUCCEEDED(sender->get_CurrentProcessId(&senderProcess)) && static_cast<DWORD>(senderProcess) == process) ++focuses; lastProcess = senderProcess; return S_OK;
    }
};
int PreviewCase(HWND window) {
    IUIAutomation* automation=nullptr; IAccessible *oldAccessible=nullptr,*restoredAccessible=nullptr;
    IUIAutomationElement *gallery=nullptr,*file=nullptr,*preview=nullptr,*image=nullptr,*secondPreview=nullptr,*secondImage=nullptr,*restored=nullptr,*button=nullptr;
    IUIAutomationCondition *listCondition=nullptr,*imageCondition=nullptr;
    IUIAutomationElementArray *files=nullptr,*images=nullptr,*restoredFiles=nullptr;
    IUIAutomationInvokePattern *open=nullptr,*buttonInvoke=nullptr;
    const auto rejectedName=[](IUIAutomationElement* element) {
        const auto deadline=GetTickCount64()+2000;
        while(GetTickCount64()<deadline) { BSTR name=nullptr;const auto hr=element->get_CurrentName(&name);if(name)SysFreeString(name);if(FAILED(hr))return true;Sleep(10); }return false;
    };
    int result=1;
    try {
        Require(SUCCEEDED(CoCreateInstance(CLSID_CUIAutomation,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&automation))),"preview UIA client");
        Require(SUCCEEDED(AccessibleObjectFromWindow(window,static_cast<DWORD>(OBJID_CLIENT),IID_IAccessible,reinterpret_cast<void**>(&oldAccessible))),"gallery MSAA before preview");
        VARIANT selected{};selected.vt=VT_I4;selected.lVal=3;Require(SUCCEEDED(oldAccessible->accSelect(SELFLAG_TAKESELECTION|SELFLAG_TAKEFOCUS,selected)),"prepare preserved gallery selection");
        Require(SUCCEEDED(automation->ElementFromHandle(window,&gallery)),"gallery root");
        VARIANT type{};type.vt=VT_I4;type.lVal=UIA_ListItemControlTypeId;automation->CreatePropertyCondition(UIA_ControlTypePropertyId,type,&listCondition);
        type.lVal=UIA_ImageControlTypeId;automation->CreatePropertyCondition(UIA_ControlTypePropertyId,type,&imageCondition);
        Require(listCondition && imageCondition && SUCCEEDED(gallery->FindAll(TreeScope_Children,listCondition,&files)),"gallery children");
        int count=0;files->get_Length(&count);Require(count==30,"gallery all30 before preview");files->GetElement(2,&file);
        Require(file && SUCCEEDED(file->GetCurrentPatternAs(UIA_InvokePatternId,IID_PPV_ARGS(&open))),"ordinary file has preview Invoke");
        Require(SUCCEEDED(open->Invoke()) && rejectedName(gallery),"enter preview retires old gallery provider");
        Require(IsWindow(window)!=FALSE,"preview preserves same native canvas");BSTR name=nullptr;
        Require(FAILED(oldAccessible->get_accName(selected,&name)) && !name,"hidden gallery MSAA names refused");
        Require(SUCCEEDED(automation->ElementFromHandle(window,&preview)),"preview root");CONTROLTYPEID control=0;preview->get_CurrentControlType(&control);Require(control==UIA_PaneControlTypeId,"preview is a pane");
        Require(SUCCEEDED(preview->FindAll(TreeScope_Children,listCondition,&images)),"hidden gallery query");images->get_Length(&count);images->Release();images=nullptr;Require(count==0,"no hidden ListItems exposed during preview");
        Require(SUCCEEDED(preview->FindAll(TreeScope_Children,imageCondition,&images)),"preview image query");images->get_Length(&count);Require(count==1,"exactly one Image in preview");images->GetElement(0,&image);
        Require(image!=nullptr,"current preview Image");name=nullptr;image->get_CurrentName(&name);const bool initial=name && wcscmp(name,L"合成图库测试条目（非产品图片）2")==0;if(name)SysFreeString(name);Require(initial,"full current preview name");
        IUIAutomationSelectionItemPattern* hiddenSelection=nullptr;const auto selectionResult=image->GetCurrentPatternAs(UIA_SelectionItemPatternId,IID_PPV_ARGS(&hiddenSelection));if(hiddenSelection)hiddenSelection->Release();Require(FAILED(selectionResult) || !hiddenSelection,"image does not expose gallery selection pattern");
        const HWND next=GetDlgItem(GetParent(window),109);if(!next)throw "native Next button";automation->ElementFromHandle(next,&button);Require(button && SUCCEEDED(button->GetCurrentPatternAs(UIA_InvokePatternId,IID_PPV_ARGS(&buttonInvoke))),"native Next Invoke");
        Require(SUCCEEDED(buttonInvoke->Invoke()) && rejectedName(image),"next retires old image provider");buttonInvoke->Release();buttonInvoke=nullptr;button->Release();button=nullptr;
        automation->ElementFromHandle(window,&secondPreview);Require(secondPreview && SUCCEEDED(secondPreview->FindAll(TreeScope_Children,imageCondition,&restoredFiles)),"next preview root");restoredFiles->GetElement(0,&secondImage);Require(secondImage!=nullptr,"next image");
        name=nullptr;secondImage->get_CurrentName(&name);const bool nextName=name && wcscmp(name,L"合成图库测试条目（非产品图片）3")==0;if(name)SysFreeString(name);Require(nextName,"next file name");
        const HWND back=GetDlgItem(GetParent(window),107);if(!back)throw "native Back button";automation->ElementFromHandle(back,&button);Require(button && SUCCEEDED(button->GetCurrentPatternAs(UIA_InvokePatternId,IID_PPV_ARGS(&buttonInvoke))),"native Back Invoke");
        Require(SUCCEEDED(buttonInvoke->Invoke()) && rejectedName(secondImage),"back retires preview Image");
        Require(SUCCEEDED(automation->ElementFromHandle(window,&restored)),"restored gallery root");restored->get_CurrentControlType(&control);Require(control==UIA_ListControlTypeId,"back restores gallery List");
        Require(SUCCEEDED(AccessibleObjectFromWindow(window,static_cast<DWORD>(OBJID_CLIENT),IID_IAccessible,reinterpret_cast<void**>(&restoredAccessible))),"restored MSAA");
        VARIANT selection{};const auto read=restoredAccessible->get_accSelection(&selection);const bool preserved=SUCCEEDED(read) && selection.vt==VT_I4 && selection.lVal==3;VariantClear(&selection);Require(preserved,"preview stepping preserves underlying selection");
        std::cout<<"external_preview: List->Pane/Image->next->List, old providers refused and selection preserved\n";result=0;
    } catch(const char* error) {std::cerr<<error<<'\n';}
    IUnknown* retained[]={open,buttonInvoke,button,restoredFiles,images,files,image,secondImage,file,restored,secondPreview,preview,gallery,listCondition,imageCondition,restoredAccessible,oldAccessible,automation};
    for(auto* value:retained)if(value)value->Release();return result;
}

}
int wmain(int argc, wchar_t** argv) {
    if (argc != 2 && argc != 3) return 2;
    const bool listen = argc == 3 && wcscmp(argv[2], L"--listener") == 0;
    const bool replace = argc == 3 && wcscmp(argv[2], L"--page") == 0;
    const bool retire = argc == 3;
    const bool msaaOnly = retire && wcscmp(argv[2], L"--msaa-retire") == 0;
    const bool elementOnly = retire && wcscmp(argv[2], L"--element-retire") == 0;
    const bool releaseFirst = retire && wcscmp(argv[2], L"--released-retire") == 0;
    const HWND window = reinterpret_cast<HWND>(_wcstoui64(argv[1], nullptr, 10));
    if (!window || !IsWindow(window) || FAILED(CoInitializeEx(nullptr, COINIT_MULTITHREADED))) return 2;
    if(argc==3 && wcscmp(argv[2],L"--preview")==0) {const int result=PreviewCase(window);CoUninitialize();return result;}
    IAccessible* accessible = nullptr; IUIAutomation* automation = nullptr; IUIAutomationElement* element = nullptr;
    IUIAutomationCondition* condition = nullptr; IUIAutomationElementArray* children = nullptr;
    IUIAutomationSelectionPattern* rootSelection = nullptr; IUIAutomationElement* firstItem = nullptr;
    IUIAutomationInvokePattern* invoke = nullptr;
    Listener* listener = nullptr;
    const auto stopListening = [&]() {
        if (!listener) return;
        if (automation) { if (element) automation->RemoveAutomationEventHandler(UIA_Selection_InvalidatedEventId,element,listener); automation->RemoveFocusChangedEventHandler(listener); }
        listener->Release(); listener = nullptr;
    };
    int stage = 10;
    try {
        if (listen) Require(SUCCEEDED(CoCreateInstance(CLSID_CUIAutomation,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&automation))),"controlled listener automation");
        if (listen) {
            listener = new Listener(); GetWindowThreadProcessId(window,&listener->process);
            Require(SUCCEEDED(automation->AddFocusChangedEventHandler(nullptr,listener)),"controlled global focus listener");
            Require(SendMessageW(GetParent(window),WM_APP+0x231,1,0) == 1,"unobserved gallery does not create root for global listener");
        }
        Require(SUCCEEDED(AccessibleObjectFromWindow(window, static_cast<DWORD>(OBJID_CLIENT), IID_IAccessible,
            reinterpret_cast<void**>(&accessible))), "external MSAA retrieval");
        LONG count = 0; Require(SUCCEEDED(accessible->get_accChildCount(&count)) && count == 30, "external MSAA children");
        VARIANT child{}; child.vt = VT_I4; child.lVal = 3;
        BSTR name = nullptr; Require(SUCCEEDED(accessible->get_accName(child, &name)) && name && SysStringLen(name) > 10, "external full name"); SysFreeString(name);
        Require(SUCCEEDED(accessible->accSelect(SELFLAG_TAKESELECTION | SELFLAG_TAKEFOCUS, child)), "external MSAA selection");
        VARIANT selection{}; Require(SUCCEEDED(accessible->get_accSelection(&selection)) && selection.vt == VT_I4 && selection.lVal == 3, "external selection readback"); VariantClear(&selection);
        VARIANT rawState{}; Require(SUCCEEDED(accessible->get_accState(child, &rawState)), "raw MSAA selected state");
        const LONG selectedState = rawState.lVal; VariantClear(&rawState);
        child.lVal = 2; Require(SUCCEEDED(accessible->get_accState(child, &rawState)), "raw MSAA unselected state");
        std::cout << "msaa_selected=0x" << std::hex << selectedState << " unselected=0x" << rawState.lVal << std::dec << "\n";
        Require((selectedState & STATE_SYSTEM_SELECTED) && !(rawState.lVal & STATE_SYSTEM_SELECTED), "MSAA selected/unselected contrast"); VariantClear(&rawState);
        if (!msaaOnly) {
        stage = 11;
        if (!automation) Require(SUCCEEDED(CoCreateInstance(CLSID_CUIAutomation, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&automation))), "UIA client creation");
        stage = 12;
        Require(SUCCEEDED(automation->ElementFromHandle(window, &element)), "UIA bridge element");
        stage = 13;
        CONTROLTYPEID type = 0; Require(SUCCEEDED(element->get_CurrentControlType(&type)) && type == UIA_ListControlTypeId, "UIA bridge List role");
        stage = 14;
        if (listen) {
            Require(SUCCEEDED(automation->AddAutomationEventHandler(UIA_Selection_InvalidatedEventId,element,TreeScope_Element,nullptr,listener)),"controlled selection listener");
            child.lVal = 4; Require(SUCCEEDED(accessible->accSelect(SELFLAG_TAKESELECTION|SELFLAG_TAKEFOCUS,child)),"observed selection/focus action");
            child.lVal = 3; Require(SUCCEEDED(accessible->accSelect(SELFLAG_TAKESELECTION|SELFLAG_TAKEFOCUS,child)),"restore observed selection");
            const ULONGLONG eventDeadline = GetTickCount64()+2000;
            while (listener->selections == 0 && GetTickCount64()<eventDeadline) Sleep(10);
            std::cout << "controlled_listener selection_events=" << listener->selections.load() << " focus_events=" << listener->focuses.load() << " calls=" << listener->focusCalls.load() << " sender_process=" << listener->lastProcess.load() << " expected_process=" << listener->process << "\n";
            Require(listener->selections > 0,"native selection event actually delivered with controlled focus subscription");
        }
        if (!elementOnly) {
        VARIANT itemType{}; itemType.vt = VT_I4; itemType.lVal = UIA_ListItemControlTypeId;
        Require(SUCCEEDED(automation->CreatePropertyCondition(UIA_ControlTypePropertyId, itemType, &condition)), "UIA list-item condition");
        stage = 15;
        Require(SUCCEEDED(element->FindAll(TreeScope_Children, condition, &children)), "UIA child enumeration");
        int length = 0; Require(SUCCEEDED(children->get_Length(&length)), "UIA child count query");
        stage = 100 + length; Require(length == 30, "native UIA exposes all current-page items");
        Require(SUCCEEDED(element->GetCurrentPatternAs(UIA_SelectionPatternId, IID_PPV_ARGS(&rootSelection))), "root Selection pattern");
        IUIAutomationElementArray* selectedItems = nullptr;
        Require(SUCCEEDED(rootSelection->GetCurrentSelection(&selectedItems)), "root selection readback");
        int selectedCount = 0; selectedItems->get_Length(&selectedCount); selectedItems->Release();
        Require(selectedCount == 1, "root selected count");
        children->GetElement(0, &firstItem);
        Require(firstItem && SUCCEEDED(firstItem->GetCurrentPatternAs(UIA_InvokePatternId, IID_PPV_ARGS(&invoke))), "directory Invoke pattern");
        for (int index : {1,2}) {
            IUIAutomationElement* item = nullptr; children->GetElement(index, &item);
            if (!item) throw "UIA item";
            IUIAutomationLegacyIAccessiblePattern* legacy = nullptr; IUIAutomationSelectionItemPattern* selectable = nullptr;
            const HRESULT legacyResult = item->GetCurrentPatternAs(UIA_LegacyIAccessiblePatternId, IID_PPV_ARGS(&legacy));
            const HRESULT selectionResult = item->GetCurrentPatternAs(UIA_SelectionItemPatternId, IID_PPV_ARGS(&selectable));
            DWORD state = 0; BOOL selected = FALSE;
            if (legacy) { legacy->get_CurrentState(&state); legacy->Release(); }
            if (selectable) { selectable->get_CurrentIsSelected(&selected); selectable->Release(); }
            item->Release();
            Require(SUCCEEDED(selectionResult) && selected == (index == 2), "native selection selected/unselected contrast");
            std::cout << "uia_index=" << index << " legacy_hr=0x" << std::hex << static_cast<ULONG>(legacyResult)
                << " legacy_state=0x" << state << " selection_hr=0x" << static_cast<ULONG>(selectionResult)
                << std::dec << " selected=" << selected << "\n";
        }
        }
        }
        if (releaseFirst) {
            if (invoke) { invoke->Release(); invoke = nullptr; }
            if (firstItem) { firstItem->Release(); firstItem = nullptr; }
            if (rootSelection) { rootSelection->Release(); rootSelection = nullptr; }
            if (children) { children->Release(); children = nullptr; }
            if (condition) { condition->Release(); condition = nullptr; }
            if (element) { element->Release(); element = nullptr; }
            if (automation) { automation->Release(); automation = nullptr; }
        }
        child.lVal = 1; stage = 16;
        Require(SUCCEEDED(invoke ? invoke->Invoke() : accessible->accDoDefaultAction(child)), "external directory action accepted");
        if (retire) {
            const ULONGLONG deadline = GetTickCount64() + 2000;
            if (!replace) {
                while (IsWindow(window) && GetTickCount64() < deadline) Sleep(10);
                Require(!IsWindow(window), "accepted action actually retired the window");
            } else {
                while (GetTickCount64() < deadline) {
                    BSTR previous = nullptr; const HRESULT current = firstItem->get_CurrentName(&previous); if (previous) SysFreeString(previous);
                    if (FAILED(current)) break; Sleep(10);
                }
                Require(IsWindow(window), "page replacement preserves window");
            }
            name = nullptr; stage = 17;
            const HRESULT stale = accessible->get_accName(child, &name);
            const bool rejected = FAILED(stale) && name == nullptr; if (name) SysFreeString(name);
            Require(rejected, "retired marshaled proxy cannot return old names");
            if (element) {
                name = nullptr; const HRESULT staleRoot = element->get_CurrentName(&name);
                const bool rootRejected = FAILED(staleRoot) && (!name || SysStringLen(name) == 0); if (name) SysFreeString(name);
                Require(rootRejected, "retired UIA root current Name refuses data");
            }
            if (firstItem) {
                name = nullptr; const HRESULT staleItem = firstItem->get_CurrentName(&name);
                const bool itemRejected = FAILED(staleItem) && (!name || SysStringLen(name) == 0); if (name) SysFreeString(name);
                Require(itemRejected, "retired UIA item current Name refuses data");
            }
            if (rootSelection) {
                IUIAutomationElementArray* staleSelection = nullptr;
                const HRESULT staleResult = rootSelection->GetCurrentSelection(&staleSelection);
                int staleCount = -1; if (staleSelection) staleSelection->get_Length(&staleCount);
                std::cout << "retired_selection_hr=0x" << std::hex << static_cast<ULONG>(staleResult) << std::dec << " count=" << staleCount << "\n";
                const bool selectionRejected = (!staleSelection || staleCount == 0); if (staleSelection) staleSelection->Release();
                Require(selectionRejected, "retired UIA current Selection refuses data");
            }
            if (replace) {
                IUIAutomationElement* replacement = nullptr; Require(SUCCEEDED(automation->ElementFromHandle(window,&replacement)), "replacement root");
                IUIAutomationElementArray* items = nullptr; const HRESULT found = replacement->FindAll(TreeScope_Children,condition,&items); replacement->Release();
                Require(SUCCEEDED(found) && items, "replacement children"); IUIAutomationElement* first = nullptr; items->GetElement(0,&first); items->Release();
                Require(first != nullptr, "replacement first child"); name = nullptr; const HRESULT named = first->get_CurrentName(&name); first->Release();
                const bool fresh = SUCCEEDED(named) && name && wcscmp(name,L"新页合成目录") == 0; if (name) SysFreeString(name);
                Require(fresh, "new provider reads replacement while old provider stays unavailable");
            }
            std::cout << "retired_names_unavailable_and_selection_empty=1\n";
        }
        stopListening(); if (invoke) invoke->Release(); if (firstItem) firstItem->Release(); if (rootSelection) rootSelection->Release();
        if (children) children->Release(); if (condition) condition->Release(); if (element) element->Release(); if (automation) automation->Release(); accessible->Release();
        CoUninitialize(); std::cout << "external MSAA/native UIA selection, invocation and retirement checks passed\n"; return 0;
    } catch (const char* error) {
        std::cerr << error << '\n'; stopListening(); if (invoke) invoke->Release(); if (firstItem) firstItem->Release(); if (rootSelection) rootSelection->Release();
        if (children) children->Release(); if (condition) condition->Release();
        if (element) element->Release(); if (automation) automation->Release(); if (accessible) accessible->Release(); CoUninitialize(); return stage;
    }
}
