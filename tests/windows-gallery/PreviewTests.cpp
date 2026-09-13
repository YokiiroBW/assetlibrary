#include "gallery/Surface.h"
#include "gallery/Uia.h"
#include "TestOwner.h"
#include "Capture.h"
#include <oleacc.h>
#include <shlobj.h>
#include <commctrl.h>
#include <windowsx.h>
#include <iostream>

namespace {
void Require(bool value,const char* message) { if (!value) throw message; }
void Pump(gallery_test::Owner& owner) {
    const auto deadline=GetTickCount64()+2000;
    while (owner.references>1 && GetTickCount64()<deadline) {
        MsgWaitForMultipleObjects(0,nullptr,FALSE,10,QS_ALLINPUT);
        MSG message{}; while (PeekMessageW(&message,nullptr,0,0,PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
    }
}
std::shared_ptr<const gallery::Pbgra> Red(UINT width,UINT height) {
    auto image=std::make_shared<gallery::Pbgra>(); image->width=width; image->height=height; image->stride=width*4;
    image->pixels.resize(static_cast<size_t>(image->stride)*height);
    for (size_t at=0;at<image->pixels.size();at+=4) { image->pixels[at+2]=255; image->pixels[at+3]=255; }
    return image;
}
snapshot::Page Page() {
    snapshot::Page page; page.status=snapshot::Status::Ready; page.epoch.Data1=7;
    for (UINT index=0;index<30;++index) {
        snapshot::Entry item; item.node.Data1=index+1; item.epoch=page.epoch;
        item.kind=index==0?snapshot::Kind::Directory:snapshot::Kind::File; item.name=L"合成大图条目"+std::to_wstring(index); page.entries.push_back(item);
    }
    return page;
}
IAccessible* Accessible(HWND canvas) {
    IAccessible* result=nullptr;
    Require(SUCCEEDED(AccessibleObjectFromWindow(canvas,static_cast<DWORD>(OBJID_CLIENT),IID_IAccessible,reinterpret_cast<void**>(&result))) && result,"native accessible object"); return result;
}
RECT ColorBounds(HWND canvas, bool green = false) {
    RECT client{}; GetClientRect(canvas,&client); gallery_test::Capture capture(client.right,client.bottom); capture.Draw(canvas);
    RECT found{client.right,client.bottom,0,0};
    for (int y=0;y<capture.height;++y) for(int x=0;x<capture.width;++x) {
        const auto at=static_cast<size_t>((y*capture.width+x)*4);
        if(capture.pixels[at]==0 && capture.pixels[at+1]==(green?255:0) && capture.pixels[at+2]==(green?0:255)) {
            found.left=std::min<LONG>(found.left,x); found.top=std::min<LONG>(found.top,y); found.right=std::max<LONG>(found.right,x+1); found.bottom=std::max<LONG>(found.bottom,y+1);
        }
    }
    return found;
}
RECT RedBounds(HWND canvas) { return ColorBounds(canvas); }
std::shared_ptr<const gallery::Pbgra> Marked() {
    auto image=std::make_shared<gallery::Pbgra>(); image->width=1600;image->height=800;image->stride=6400;image->pixels.resize(5120000);
    for(UINT y=0;y<800;++y)for(UINT x=0;x<1600;++x) {
        const size_t at=static_cast<size_t>(y)*image->stride+x*4;
        image->pixels[at+1]=(x>=780 && x<820 && y>=380 && y<420)?255:0;
        image->pixels[at+2]=image->pixels[at+1]?0:255;image->pixels[at+3]=255;
    }
    return image;
}
std::wstring Summary(gallery::Surface* surface) {
    wchar_t text[257]{}; GetWindowTextW(GetDlgItem(surface->Window(),gallery::StatusTextControlId),text,257);return text;
}
bool Key(gallery::Surface* surface,HWND window,WPARAM key,int modifier=0) {
    BYTE saved[256]{},state[256]{};Require(GetKeyboardState(saved)!=FALSE,"read thread keyboard state");CopyMemory(state,saved,sizeof(state));
    state[VK_CONTROL]=state[VK_MENU]=state[VK_SHIFT]=0;if(modifier)state[modifier]=0x80;SetKeyboardState(state);
    MSG message{};message.hwnd=window;message.message=WM_KEYDOWN;message.wParam=key;
    const bool handled=surface->TranslateAccelerator(message);SetKeyboardState(saved);return handled;
}
void Wheel(HWND canvas,int delta,POINT point,WORD modifiers=0,HWND receiver=nullptr) {
    ClientToScreen(canvas,&point);SendMessageW(receiver?receiver:canvas,WM_MOUSEWHEEL,MAKEWPARAM(modifiers,static_cast<WORD>(delta)),MAKELPARAM(point.x,point.y));
}
POINT Center(const RECT& rectangle) { return {(rectangle.left+rectangle.right)/2,(rectangle.top+rectangle.bottom)/2}; }
struct Scenario { gallery::Surface* surface=nullptr; UINT activated=0,preferences=0,closed=0,viewports=0; int step=0; bool deleteOnViewport=false,deleteOnFocus=false; };
LRESULT CALLBACK DeleteOnCapture(HWND window,UINT message,WPARAM wParam,LPARAM lParam,UINT_PTR,DWORD_PTR reference) {
    if(message==WM_CAPTURECHANGED) { auto& scenario=*reinterpret_cast<Scenario*>(reference);auto* retired=scenario.surface;scenario.surface=nullptr;delete retired; }
    return DefSubclassProc(window,message,wParam,lParam);
}
}
int main() {
    if (FAILED(CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED))) return 2;
    gallery_test::Owner owner; gallery::Surface* surface=nullptr; HWND parent=nullptr; IAccessible *old=nullptr,*preview=nullptr;
    Scenario scenario; int result=1;
    try {
        parent=CreateWindowExW(0,L"STATIC",L"hidden preview component",WS_OVERLAPPEDWINDOW,0,0,1000,700,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
        if(!parent) throw "parent";
        gallery::Callbacks callbacks; callbacks.context=&scenario; callbacks.lifetimeOwner=callbacks.providerLifetimeOwner=&owner;
        callbacks.activateItem=[](void* context,UINT index) noexcept { static_cast<Scenario*>(context)->activated=index+1; };
        callbacks.preferencesChanged=[](void* context) noexcept { ++static_cast<Scenario*>(context)->preferences; };
        callbacks.previewStep=[](void* context,int delta) noexcept { static_cast<Scenario*>(context)->step=delta; };
        callbacks.previewClose=[](void* context) noexcept { auto& current=*static_cast<Scenario*>(context); ++current.closed; current.surface->EndPreview(); };
        callbacks.viewportChanged=[](void* context) noexcept { auto& current=*static_cast<Scenario*>(context); ++current.viewports; if(current.deleteOnViewport && current.surface) { auto* retired=current.surface;current.surface=nullptr;delete retired; } };
        callbacks.focusActivated=[](void* context) noexcept { auto& current=*static_cast<Scenario*>(context);if(current.deleteOnFocus && current.surface) { auto* retired=current.surface;current.surface=nullptr;delete retired; } };
        RECT bounds{0,0,1000,700}; Require(SUCCEEDED(gallery::Surface::Create(parent,bounds,callbacks,&surface)),"surface"); scenario.surface=surface;
        auto page=Page(); surface->SetPage(page,3); surface->SetStatusText(L"当前页30项，已选2项");
        const HWND canvas=FindWindowExW(surface->Window(),nullptr,L"STATIC",nullptr); if(!canvas) throw "canvas";
        surface->SetMode(gallery::Mode::Gallery); surface->SetDensity(gallery::DefaultDensityDip); Require(scenario.preferences==0,"unchanged preferences do not notify");
        surface->SetMode(gallery::Mode::List); surface->SetMode(gallery::Mode::List); surface->SetDensity(96); surface->SetDensity(1);
        Require(scenario.preferences==2,"only actual bounded mode/density changes notify");
        surface->SetMode(gallery::Mode::Gallery); surface->SetDensity(176); scenario.preferences=0;
        surface->SelectItem(0,SVSI_SELECT|SVSI_DESELECTOTHERS|SVSI_FOCUSED); MSG directorySpace{};directorySpace.hwnd=canvas;directorySpace.message=WM_KEYDOWN;directorySpace.wParam=VK_SPACE;
        surface->TranslateAccelerator(directorySpace);Require(scenario.activated==0,"Space on directory never changes navigation history");
        surface->SelectItem(1,SVSI_SELECT|SVSI_DESELECTOTHERS|SVSI_FOCUSED|SVSI_SELECTIONMARK); surface->SelectItem(2,SVSI_SELECT);
        MSG key{}; key.hwnd=canvas; key.message=WM_KEYDOWN; key.wParam=VK_SPACE;
        Require(surface->TranslateAccelerator(key) && scenario.activated==2,"Space activates a regular file");
        BYTE saved[256]{},modified[256]{}; Require(GetKeyboardState(saved)!=FALSE,"read isolated keyboard state"); CopyMemory(modified,saved,sizeof(saved)); modified[VK_CONTROL]=0x80; SetKeyboardState(modified);
        scenario.activated=0; surface->TranslateAccelerator(key); key.wParam=VK_RETURN; const bool ctrlEnter=surface->TranslateAccelerator(key); SetKeyboardState(saved);
        Require(!ctrlEnter && scenario.activated==0 && surface->SelectedItems().count==1,"Ctrl+Space toggles selection; Ctrl+Enter does not open preview");
        surface->SelectItem(1,SVSI_SELECT|SVSI_FOCUSED|SVSI_ENSUREVISIBLE); SendMessageW(canvas,WM_VSCROLL,SB_LINEDOWN,0);
        const auto selected=surface->SelectedItems(); const int focus=surface->FocusedItem(); const int scroll=GetScrollPos(canvas,SB_VERT);
        auto visible=surface->VisibleFileItems(); if(visible.count) surface->SetThumbnail(visible.indices[0],3,Red(128,64));
        old=Accessible(canvas); Require(surface->BeginPreview(0,1,false,true)==E_INVALIDARG,"directory cannot become image preview");
        Require(SUCCEEDED(surface->BeginPreview(1,1,false,true)) && surface->Previewing(),"begin preview");
        Require(surface->VisibleFileItems().count==0 && surface->RetainedImageBytes()==0,"begin cancels all thumbnail candidates and releases pixels");
        Require(surface->SelectedItems().count==selected.count && surface->FocusedItem()==focus,"preview preserves underlying selection/focus");
        VARIANT child{};child.vt=VT_I4;child.lVal=1;BSTR name=nullptr;
        Require(FAILED(old->get_accName(child,&name)) && !name,"hidden gallery provider cannot read names"); old->Release();old=nullptr;
        preview=Accessible(canvas); LONG count=0; preview->get_accChildCount(&count);Require(count==1,"preview exposes only current image");
        preview->get_accName(child,&name);const bool currentName=name && page.entries[1].name==name;if(name)SysFreeString(name);Require(currentName,"preview image full name");
        VARIANT role{};preview->get_accRole(child,&role);Require(role.vt==VT_I4 && role.lVal==ROLE_SYSTEM_GRAPHIC,"image accessibility role");VariantClear(&role);
        Require(surface->SetThumbnail(1,3,Red(128,64))==S_FALSE,"thumbnail completion cannot publish during preview");
        auto image=Red(1600,800);std::weak_ptr<const gallery::Pbgra> retained=image;
        Require(SUCCEEDED(surface->SetPreview(1,1,image,L"大图已就绪")),"1600px preview accepted");image.reset();
        const RECT red=RedBounds(canvas);Require(red.right>red.left && std::abs((red.right-red.left)-2*(red.bottom-red.top))<=2,"fit preserves wide-image aspect ratio");
        const auto requestsBefore=scenario.viewports;
        Require(Summary(surface).find(L"派生图 1600×800")!=std::wstring::npos,"summary distinguishes derived pixels");
        surface->SetPreview(1,1,Marked(),L"锚点与捕获验证");
        Require(Key(surface,canvas,'1') && Summary(surface).find(L"100%")!=std::wstring::npos,"100 percent command");
        auto marker=ColorBounds(canvas,true); const POINT markerCenter=Center(marker),wheelAnchor{markerCenter.x-100,markerCenter.y-40};
        const auto initialSummary=Summary(surface);Wheel(canvas,0,wheelAnchor);Wheel(canvas,60,wheelAnchor);
        Require(Summary(surface)==initialSummary,"zero and fractional wheel do not change scale");
        Wheel(canvas,60,wheelAnchor,MK_CONTROL);marker=ColorBounds(canvas,true);
        Require(Summary(surface).find(L"125%")!=std::wstring::npos && std::abs(Center(marker).x-markerCenter.x-25)<=2 && std::abs(Center(marker).y-markerCenter.y-10)<=2,"two fragments zoom around cursor without recentering");
        Wheel(canvas,-120,wheelAnchor);Require(Summary(surface).find(L"100%")!=std::wstring::npos,"negative wheel zoom");
        const HWND focusedButton=GetDlgItem(surface->Window(),gallery::PreviewFitControlId);SetFocus(focusedButton);
        Wheel(canvas,120,wheelAnchor,0,focusedButton);Require(Summary(surface).find(L"125%")!=std::wstring::npos,"button-focused wheel bubbles to root and is consumed exactly once");
        Key(surface,canvas,'1');
        marker=ColorBounds(canvas,true);const auto dragStart=Center(marker);
        SendMessageW(canvas,WM_LBUTTONDOWN,MK_LBUTTON,MAKELPARAM(dragStart.x,dragStart.y));Require(GetCapture()==canvas,"drag obtains native capture");
        SendMessageW(canvas,WM_MOUSEMOVE,MK_LBUTTON,MAKELPARAM(dragStart.x+45,dragStart.y+20));
        auto moved=ColorBounds(canvas,true);Require(std::abs(Center(moved).x-Center(marker).x-45)<=1 && std::abs(Center(moved).y-Center(marker).y-20)<=1,"captured pointer moves image");
        SendMessageW(canvas,WM_LBUTTONUP,0,MAKELPARAM(dragStart.x+45,dragStart.y+20));Require(GetCapture()!=canvas,"button up releases capture");
        for(UINT cancel : {WM_CANCELMODE,WM_KILLFOCUS,WM_CAPTURECHANGED}) {
            SendMessageW(canvas,WM_LBUTTONDOWN,MK_LBUTTON,MAKELPARAM(dragStart.x,dragStart.y));
            if(cancel==WM_CAPTURECHANGED)SetCapture(parent);else if(cancel==WM_KILLFOCUS)SetFocus(parent);else SendMessageW(canvas,cancel,0,0);
            Require(GetCapture()!=canvas,"cancel/focus/capture loss ends dragging");
            const auto before=ColorBounds(canvas,true);SendMessageW(canvas,WM_MOUSEMOVE,MK_LBUTTON,MAKELPARAM(dragStart.x+90,dragStart.y+50));
            const auto after=ColorBounds(canvas,true);Require(EqualRect(&before,&after)!=FALSE,"canceled pointer move cannot pan");
            if(GetCapture()==parent)ReleaseCapture();
        }
        const auto panBefore=ColorBounds(canvas,true);Require(Key(surface,canvas,VK_LEFT,VK_SHIFT),"Shift arrow handled");const auto panAfter=ColorBounds(canvas,true);
        Require(Center(panAfter).x>Center(panBefore).x && scenario.step==0,"Shift arrow pans instead of switching files");
        Require(!Key(surface,canvas,VK_ADD,VK_CONTROL) && !Key(surface,canvas,'1',VK_MENU),"modified transform shortcuts remain with host");
        Key(surface,canvas,'0');const auto fitSummary=Summary(surface);SendMessageW(canvas,WM_LBUTTONDBLCLK,MK_LBUTTON,MAKELPARAM(dragStart.x,dragStart.y));
        Require(Summary(surface).find(L"缩放 100%")!=std::wstring::npos,"double click enters actual pixels");
        SendMessageW(canvas,WM_LBUTTONDBLCLK,MK_LBUTTON,MAKELPARAM(dragStart.x,dragStart.y));Require(Summary(surface)==fitSummary,"double click returns to fit");
        for(int at=0;at<16;++at)Key(surface,canvas,VK_ADD);
        Require(Summary(surface).find(L"缩放 400%")!=std::wstring::npos && !IsWindowEnabled(GetDlgItem(surface->Window(),gallery::PreviewZoomInControlId)),"400 percent disables zoom in");
        const RECT clipped=RedBounds(canvas);RECT client{};GetClientRect(canvas,&client);const int dpiScale=MulDiv(12,static_cast<int>(GetDpiForWindow(parent)),96);
        Require(clipped.left>=dpiScale && clipped.top>=MulDiv(40,static_cast<int>(GetDpiForWindow(parent)),96) && clipped.right<=client.right-dpiScale && clipped.bottom<=client.bottom-dpiScale,"zoom remains clipped below name and inside picture margins");
        Key(surface,canvas,'0');for(int at=0;at<20;++at)Key(surface,canvas,VK_SUBTRACT);
        Require(Summary(surface).find(L"缩放 10%")!=std::wstring::npos && !IsWindowEnabled(GetDlgItem(surface->Window(),gallery::PreviewZoomOutControlId)),"10 percent disables zoom out");
        Key(surface,canvas,'0');Require(scenario.viewports==requestsBefore && scenario.preferences==0,"transforms do not request images or persist preferences");
        image=Red(1600,800);retained=image;Require(SUCCEEDED(surface->SetPreview(1,1,image,L"大图已就绪")),"restore image for retained baseline");image.reset();
        const DWORD gdi=GetGuiResources(GetCurrentProcess(),GR_GDIOBJECTS);for(int at=0;at<8;++at)RedBounds(canvas);Require(GetGuiResources(GetCurrentProcess(),GR_GDIOBJECTS)==gdi,"preview painting releases temporary GDI buffers");
        CopyMemory(modified,saved,sizeof(saved));modified[VK_CONTROL]=0x80;SetKeyboardState(modified);
        key.wParam=VK_ESCAPE;const bool ctrlEscape=surface->TranslateAccelerator(key);key.wParam=VK_RIGHT;const bool ctrlRight=surface->TranslateAccelerator(key);
        modified[VK_CONTROL]=0;modified[VK_MENU]=0x80;SetKeyboardState(modified);key.wParam=VK_LEFT;const bool altLeft=surface->TranslateAccelerator(key);SetKeyboardState(saved);
        Require(!ctrlEscape && !ctrlRight && !altLeft && scenario.closed==0 && scenario.step==0,"preview preserves modified Explorer navigation shortcuts");
        key.wParam=VK_TAB;Require(surface->TranslateAccelerator(key) && GetFocus()==GetDlgItem(surface->Window(),gallery::PreviewBackControlId),"preview Tab reaches Back");
        key.hwnd=GetFocus();Require(surface->TranslateAccelerator(key) && GetFocus()==GetDlgItem(surface->Window(),gallery::PreviewNextControlId),"preview Tab skips disabled Previous");
        for(int id : {gallery::PreviewZoomOutControlId,gallery::PreviewZoomInControlId,gallery::PreviewFitControlId,gallery::PreviewActualControlId}) {
            key.hwnd=GetFocus();Require(surface->TranslateAccelerator(key) && GetFocus()==GetDlgItem(surface->Window(),id),"Tab reaches every enabled native transform button");
            wchar_t label[40]{};Require(GetWindowTextW(GetFocus(),label,40)>0,"native transform button has accessible text");
        }
        key.hwnd=GetFocus();Require(!surface->TranslateAccelerator(key),"preview boundary Tab remains with host");key.hwnd=canvas;
        key.wParam=VK_LEFT;surface->TranslateAccelerator(key);Require(scenario.step==0,"disabled previous boundary");
        key.wParam=VK_RIGHT;surface->TranslateAccelerator(key);Require(scenario.step==1,"keyboard next callback");scenario.step=0;
        SendMessageW(GetDlgItem(surface->Window(),gallery::PreviewNextControlId),BM_CLICK,0,0);Require(scenario.step==1,"native next button");
        Key(surface,canvas,'1');SendMessageW(canvas,WM_LBUTTONDOWN,MK_LBUTTON,MAKELPARAM(dragStart.x,dragStart.y));Require(GetCapture()==canvas,"capture before switch");
        Require(SUCCEEDED(surface->BeginPreview(2,2,true,false)) && retained.expired() && GetCapture()!=canvas,"switch releases old image and capture immediately");
        Require(Summary(surface).find(L'%')==std::wstring::npos && !IsWindowEnabled(GetDlgItem(surface->Window(),gallery::PreviewActualControlId)),"loading has no old scale or enabled transform");
        name=nullptr;Require(FAILED(preview->get_accName(child,&name)) && !name,"old preview provider retired on switch");preview->Release();preview=nullptr;
        Require(surface->SetPreview(1,1,Red(100,100),L"stale")==S_FALSE,"old serial and index cannot return stale pixels");
        Require(SUCCEEDED(surface->SetPreview(2,2,Red(1600,1600),L"最大派生图")) && surface->RetainedImageBytes()==gallery::MaxPreviewPixelBytes,"maximum preview fits existing persistent budget");
        Key(surface,canvas,'1');SendMessageW(canvas,WM_LBUTTONDOWN,MK_LBUTTON,MAKELPARAM(dragStart.x,dragStart.y));
        auto oversized=std::make_shared<gallery::Pbgra>();oversized->width=1601;oversized->height=1;oversized->stride=6404;oversized->pixels.resize(6404);
        Require(surface->SetPreview(2,2,oversized,L"invalid")==E_INVALIDARG && surface->RetainedImageBytes()==0 && GetCapture()!=canvas && Summary(surface).find(L'%')==std::wstring::npos,"invalid preview clears pixels, scale and capture");
        auto capacity=std::make_shared<gallery::Pbgra>();capacity->width=capacity->height=1;capacity->stride=4;capacity->pixels.resize(4);capacity->pixels.reserve(gallery::MaxImageBytes+1);
        Require(surface->SetPreview(2,2,capacity,L"too large")==E_OUTOFMEMORY && surface->RetainedImageBytes()==0,"preview capacity retains 16MiB limit");
        surface->SetPreview(2,2,nullptr,L"无权访问此预览");key.wParam=VK_ESCAPE;Require(surface->TranslateAccelerator(key) && scenario.closed==1 && !surface->Previewing(),"Escape closes through owner callback");
        Require(surface->SelectedItems().count==selected.count && surface->FocusedItem()==focus && GetScrollPos(canvas,SB_VERT)==scroll,"close restores exact unchanged browse state");
        Require(surface->VisibleFileItems().count>0 && GetFocus()==canvas,"close restores focus and thumbnail viewport");
        Require(surface->SetPreview(2,2,Red(1,1),L"late")==S_FALSE,"completion after close is rejected");
        surface->BeginPreview(1,3,false,true);surface->SetPreview(1,3,Red(1600,1),L"横条");const auto horizontal=RedBounds(canvas);Require(horizontal.bottom-horizontal.top==1,"extreme horizontal preview uses contain");
        surface->SetPreview(1,3,Red(1,1600),L"竖条");const auto vertical=RedBounds(canvas);Require(vertical.right-vertical.left==1,"extreme vertical preview uses contain");
        surface->SetVisible(false);Require(!surface->Previewing() && surface->RetainedImageBytes()==0,"hide retires preview pixels");surface->SetVisible(true);
        surface->BeginPreview(1,4,false,true);surface->Clear(snapshot::Status::AccessDenied,4);Require(!surface->Previewing() && surface->RetainedImageBytes()==0,"permission clear closes preview");
        surface->SetPage(page,5);scenario.deleteOnViewport=true;surface->BeginPreview(1,5,false,true);surface=scenario.surface;Require(!surface,"begin callback may destroy Surface");
        scenario.deleteOnViewport=false;
        for(bool captureReentry : {true,false}) {
            Require(SUCCEEDED(gallery::Surface::Create(parent,bounds,callbacks,&surface)),"reentry surface");scenario.surface=surface;
            surface->SetPage(page,1);surface->BeginPreview(1,1,true,true);surface->SetPreview(1,1,Red(1600,800),L"重入验证");
            const HWND target=FindWindowExW(surface->Window(),nullptr,L"STATIC",nullptr);Key(surface,target,'1');
            HWND tabAt=target;
            for(int id=gallery::PreviewBackControlId;id<=gallery::PreviewActualControlId;++id) {
                Require(Key(surface,tabAt,VK_TAB) && GetFocus()==GetDlgItem(surface->Window(),id),"all-enabled Tab order fits canvas plus seven controls");tabAt=GetFocus();
            }
            Require(!Key(surface,tabAt,VK_TAB),"eighth focus entry yields to host at boundary");
            if(captureReentry) {
                Require(SetWindowSubclass(parent,DeleteOnCapture,77,reinterpret_cast<DWORD_PTR>(&scenario))!=FALSE,"capture callback fixture");SetCapture(parent);
            } else { SetFocus(parent);scenario.deleteOnFocus=true; }
            SendMessageW(target,WM_LBUTTONDOWN,MK_LBUTTON,MAKELPARAM(300,200));surface=scenario.surface;
            Require(!surface && !IsWindow(target) && GetCapture()!=target,"SetCapture/SetFocus callback can destroy Surface without retaining stale drag");
            if(captureReentry)RemoveWindowSubclass(parent,DeleteOnCapture,77);scenario.deleteOnFocus=false;
        }
        Pump(owner);Require(owner.references==1 && !owner.wrongThread && gallery::InspectUia().providers==0 && gallery::InspectUia().dispatcherWindows==0,"preview owner/provider resources reclaimed");
        std::cout<<"gallery_preview: fit, input, browse restoration, accessibility retirement, stale completion and budgets passed\n";result=0;
    } catch(const char* message) { std::cerr<<message<<'\n'; }
    if(old)old->Release();if(preview)preview->Release();if(surface)delete surface;Pump(owner);if(parent)DestroyWindow(parent);CoUninitialize();return result;
}
