#include "SnapshotTestSupport.h"
#include <shlguid.h>
#include <servprov.h>
#include <shellapi.h>
#include <functional>

namespace {
using proof::Check;
struct Menu {
    HMENU value=CreatePopupMenu();
    Menu(){if(!value)throw std::runtime_error("CreatePopupMenu");}
    ~Menu(){DestroyMenu(value);}
    Menu(const Menu&)=delete;Menu& operator=(const Menu&)=delete;
};
// Only a COM service recorder. It has no Explorer window, transport or file path.
class Browser final : public IShellBrowser, public IServiceProvider {
public:
    ULONG refs=1,calls=0;UINT flags=0;PIDLIST_ABSOLUTE last=nullptr;
    HRESULT serviceResult=S_OK,browseResult=S_OK;
    std::function<void()> duringQuery,duringBrowse;
    ~Browser(){CoTaskMemFree(last);}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** result) override {
        if(!result)return E_POINTER;*result=nullptr;
        if(iid==IID_IUnknown||iid==IID_IShellBrowser||iid==IID_IOleWindow)*result=static_cast<IShellBrowser*>(this);
        else if(iid==IID_IServiceProvider)*result=static_cast<IServiceProvider*>(this);else return E_NOINTERFACE;
        AddRef();return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override {return ++refs;}
    ULONG STDMETHODCALLTYPE Release() override {const auto count=--refs;if(!count)delete this;return count;}
    HRESULT STDMETHODCALLTYPE QueryService(REFGUID service,REFIID iid,void** result) override {
        if(!result)return E_POINTER;*result=nullptr;
        if(service!=SID_STopLevelBrowser||iid!=IID_IShellBrowser)return E_NOINTERFACE;
        if(duringQuery)duringQuery();
        if(FAILED(serviceResult))return serviceResult;return QueryInterface(iid,result);
    }
    HRESULT STDMETHODCALLTYPE BrowseObject(PCUIDLIST_RELATIVE target,UINT requested) override {
        if(duringBrowse)duringBrowse();
        ++calls;flags=requested;CoTaskMemFree(last);last=ILCloneFull(target);return last?browseResult:E_OUTOFMEMORY;
    }
    HRESULT STDMETHODCALLTYPE GetWindow(HWND* result) override {if(result)*result=nullptr;return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE ContextSensitiveHelp(BOOL) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE InsertMenusSB(HMENU,LPOLEMENUGROUPWIDTHS) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE SetMenuSB(HMENU,HOLEMENU,HWND) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE RemoveMenusSB(HMENU) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE SetStatusTextSB(LPCWSTR) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE EnableModelessSB(BOOL) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE TranslateAcceleratorSB(MSG*,WORD) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetViewStateStream(DWORD,IStream** result) override {if(result)*result=nullptr;return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetControlWindow(UINT,HWND* result) override {if(result)*result=nullptr;return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE SendControlMsg(UINT,UINT,WPARAM,LPARAM,LRESULT*) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE QueryActiveShellView(IShellView** result) override {if(result)*result=nullptr;return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE OnViewWindowActive(IShellView*) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE SetToolbarItems(LPTBBUTTONSB,UINT,UINT) override {return E_NOTIMPL;}
};
void OpenKind(proof::Library& library,snapshot::Kind kind){
    auto root=library.Root();
    snapshot::Entry parent{{101},{102},snapshot::Kind::Library,L"parent"},entry{{101},{103},kind,L"child"};
    proof::Item parentItem(snapshot::MakePidl(parent)),item(snapshot::MakePidl(entry));
    auto folder=proof::Bind(root.value,parentItem);
    proof::Item expected(ILCombine(parentItem.value,item.value));
    PCUITEMID_CHILD raw=item.value;proof::Com<IContextMenu> context;
    Check(SUCCEEDED(folder.value->GetUIObjectOf(nullptr,1,&raw,IID_IContextMenu,nullptr,reinterpret_cast<void**>(&context.value))),"menu acquisition");
    proof::Com<IObjectWithSite> site;Check(SUCCEEDED(context.value->QueryInterface(IID_PPV_ARGS(&site.value))),"menu site interface");
    // The menu owns its absolute target and its folder even after both inputs die.
    CoTaskMemFree(item.value);item.value=nullptr;folder.value->Release();folder.value=nullptr;root.value->Release();root.value=nullptr;
    Check(library.canUnload()==S_FALSE,"menu retains DLL owner");
    for(UINT option:{UINT(CMF_NORMAL),UINT(CMF_DEFAULTONLY)}){
        Menu menu;auto hr=context.value->QueryContextMenu(menu.value,0,73,73,option);
        Check(SUCCEEDED(hr)&&HRESULT_CODE(hr)==1&&GetMenuItemCount(menu.value)==1,"exactly one verb and offset count");
        Check(GetMenuDefaultItem(menu.value,FALSE,0)==73,"open is default, including double-click");
        wchar_t label[40]{};Check(GetMenuStringW(menu.value,73,label,40,MF_BYCOMMAND)>0&&wcscmp(label,L"打开(&O)")==0,"open label");
    }
    {Menu menu;Check(FAILED(context.value->QueryContextMenu(menu.value,0,74,73,CMF_NORMAL))&&GetMenuItemCount(menu.value)==0,"command range bound");}
    for(UINT option:{UINT(CMF_NODEFAULT),UINT(CMF_DONOTPICKDEFAULT)}){
        Menu menu;Check(SUCCEEDED(context.value->QueryContextMenu(menu.value,0,1,1,option))&&GetMenuDefaultItem(menu.value,FALSE,0)==static_cast<UINT>(-1),"caller can suppress default");
    }
    char ansi[8]{};wchar_t wide[8]{};
    Check(context.value->GetCommandString(0,GCS_VERBA,nullptr,ansi,8)==S_OK&&strcmp(ansi,"open")==0,"ANSI canonical verb");
    Check(context.value->GetCommandString(0,GCS_VERBW,nullptr,reinterpret_cast<LPSTR>(wide),8)==S_OK&&wcscmp(wide,L"open")==0,"Unicode canonical verb");
    Check(FAILED(context.value->GetCommandString(0,GCS_VERBA,nullptr,ansi,2)),"bounded verb output");
    Check(context.value->GetCommandString(0,GCS_VALIDATEW,nullptr,nullptr,0)==S_OK&&FAILED(context.value->GetCommandString(1,GCS_VALIDATEW,nullptr,nullptr,0)),"only offset zero validates");
    CMINVOKECOMMANDINFO info{};info.cbSize=sizeof(info);info.lpVerb=MAKEINTRESOURCEA(0);
    Check(context.value->InvokeCommand(&info)==E_NOINTERFACE,"missing browser site fails");
    proof::Com<Browser> browser;browser.value=new Browser();
    Check(site.value->SetSite(static_cast<IShellBrowser*>(browser.value))==S_OK&&browser.value->refs==2,"site retained");
    Check(site.value->SetSite(static_cast<IShellBrowser*>(browser.value))==S_OK&&browser.value->refs==2,"same site replacement balanced");
    {proof::Com<IShellBrowser> read;Check(SUCCEEDED(site.value->GetSite(IID_PPV_ARGS(&read.value))),"GetSite");}
    Check(context.value->InvokeCommand(&info)==S_OK,"numeric default invoke");
    Check(browser.value->calls==1&&browser.value->flags==(SBSP_ABSOLUTE|SBSP_SAMEBROWSER)&&ILIsEqual(browser.value->last,expected.value),"exact target, current window only");
    info.lpVerb="OPEN";Check(context.value->InvokeCommand(&info)==S_OK,"ANSI open invoke");
    CMINVOKECOMMANDINFOEX unicode{};unicode.cbSize=sizeof(unicode);unicode.fMask=CMIC_MASK_UNICODE;unicode.lpVerbW=L"open";
    Check(context.value->InvokeCommand(reinterpret_cast<CMINVOKECOMMANDINFO*>(&unicode))==S_OK,"Unicode open invoke");
    struct Mixed { LPCSTR ansi;LPCWSTR wide;bool allowed; };
    const Mixed mixed[]={
        {"delete",nullptr,false},{"open",nullptr,true},
        {MAKEINTRESOURCEA(1),nullptr,false},{MAKEINTRESOURCEA(0),nullptr,true},
        {MAKEINTRESOURCEA(1),L"open",true},{"delete",L"open",false},
        {"open",L"delete",false},{MAKEINTRESOURCEA(0),L"delete",false},
        {"open",L"OPEN",true},{MAKEINTRESOURCEA(0),MAKEINTRESOURCEW(1),true}
    };
    for(const auto& row:mixed){
        CMINVOKECOMMANDINFOEX command{};command.cbSize=sizeof(command);command.fMask=CMIC_MASK_UNICODE;
        command.lpVerb=row.ansi;command.lpVerbW=row.wide;const auto calls=browser.value->calls;
        auto hr=context.value->InvokeCommand(reinterpret_cast<CMINVOKECOMMANDINFO*>(&command));
        Check(row.allowed?hr==S_OK:FAILED(hr),"mixed ANSI/Unicode verb decision");
        Check(browser.value->calls==calls+(row.allowed?1:0),"mixed field navigation count");
    }
    const auto before=browser.value->calls;
    for(const char* verb:{"delete","copy","rename","paste","opennew","properties","open-extra"}){info.lpVerb=verb;Check(FAILED(context.value->InvokeCommand(&info)),"unrecognized verb denied");}
    info.lpVerb=MAKEINTRESOURCEA(1);Check(FAILED(context.value->InvokeCommand(&info)),"numeric nonzero denied");
    unicode.lpVerbW=L"delete";Check(FAILED(context.value->InvokeCommand(reinterpret_cast<CMINVOKECOMMANDINFO*>(&unicode))),"Unicode write denied");
    info.lpVerb="open";info.fMask=CMIC_MASK_UNICODE;Check(context.value->InvokeCommand(&info)==E_INVALIDARG,"Unicode short structure rejected");
    info.fMask=0;info.cbSize=0;Check(context.value->InvokeCommand(&info)==E_INVALIDARG&&context.value->InvokeCommand(nullptr)==E_INVALIDARG,"invalid structure rejected");
    Check(browser.value->calls==before,"negative verbs perform no navigation");
    info.cbSize=sizeof(info);browser.value->serviceResult=E_ACCESSDENIED;
    Check(context.value->InvokeCommand(&info)==E_ACCESSDENIED&&browser.value->calls==before,"service failure propagated");
    browser.value->serviceResult=S_OK;browser.value->browseResult=E_ABORT;
    Check(context.value->InvokeCommand(&info)==E_ABORT&&browser.value->calls==before+1,"navigation failure propagated");
    Check(site.value->SetSite(nullptr)==S_OK&&browser.value->refs==1,"site cleared");
    {proof::Com<IShellBrowser> missing;Check(FAILED(site.value->GetSite(IID_PPV_ARGS(&missing.value)))&&!missing.value,"cleared GetSite");}
    Check(context.value->InvokeCommand(&info)==E_NOINTERFACE,"cleared site cannot invoke");
}
void NegativeKinds(proof::Library& library){
    auto root=library.Root();
    for(auto kind:{snapshot::Kind::File,snapshot::Kind::Reparse,snapshot::Kind::StatusRow}){
        snapshot::Entry entry{{101},{103},kind,L"non-navigable"};
        if(kind==snapshot::Kind::StatusRow){entry.node={};entry.status=snapshot::Status::Unavailable;}
        proof::Item item(snapshot::MakePidl(entry));PCUITEMID_CHILD raw=item.value;proof::Com<IContextMenu> context;
        Check(root.value->GetUIObjectOf(nullptr,1,&raw,IID_IContextMenu,nullptr,reinterpret_cast<void**>(&context.value))==E_NOINTERFACE&&!context.value,"non-navigable kinds have no menu");
    }
    snapshot::Entry entry{{101},{103},snapshot::Kind::Directory,L"folder"};proof::Item item(snapshot::MakePidl(entry));
    PCUITEMID_CHILD raw=item.value;PCUITEMID_CHILD many[]={raw,raw};proof::Com<IContextMenu> context;
    Check(root.value->GetUIObjectOf(nullptr,2,many,IID_IContextMenu,nullptr,reinterpret_cast<void**>(&context.value))==E_NOINTERFACE,"multiple selection has no default navigation");
    Check(root.value->GetUIObjectOf(nullptr,0,nullptr,IID_IContextMenu,nullptr,reinterpret_cast<void**>(&context.value))==E_NOINTERFACE,"background has no item navigation");
    Check(root.value->GetUIObjectOf(nullptr,1,&raw,IID_IDataObject,nullptr,reinterpret_cast<void**>(&context.value))==E_NOINTERFACE,"no clipboard interface");
    proof::Item nested(ILCombine(raw,raw));raw=nested.value;
    Check(root.value->GetUIObjectOf(nullptr,1,&raw,IID_IContextMenu,nullptr,reinterpret_cast<void**>(&context.value))==E_INVALIDARG,"child must have one segment");
    raw=item.value;reinterpret_cast<BYTE*>(item.value)[6]=2;
    Check(root.value->GetUIObjectOf(nullptr,1,&raw,IID_IContextMenu,nullptr,reinterpret_cast<void**>(&context.value))==E_INVALIDARG,"malformed child denied");
}
void Icons(proof::Library& library){
    auto root=library.Root();
    for(auto kind:{snapshot::Kind::Library,snapshot::Kind::Directory,snapshot::Kind::NextPage,snapshot::Kind::File,snapshot::Kind::Reparse,snapshot::Kind::StatusRow}){
        snapshot::Entry entry{{201},{202},kind,L"display-only"};
        if(kind==snapshot::Kind::StatusRow){entry.node={};entry.status=snapshot::Status::Expired;}
        proof::Item item(snapshot::MakePidl(entry));PCUITEMID_CHILD raw=item.value;proof::Com<IExtractIconW> icon;
        Check(SUCCEEDED(root.value->GetUIObjectOf(nullptr,1,&raw,IID_IExtractIconW,nullptr,reinterpret_cast<void**>(&icon.value))),"standard icon acquisition");
        const auto stockId=(kind==snapshot::Kind::Library||kind==snapshot::Kind::Directory||kind==snapshot::Kind::NextPage)?SIID_FOLDER:SIID_DOCNOASSOC;
        SHSTOCKICONINFO expected{sizeof(expected)};Check(SUCCEEDED(SHGetStockIconInfo(stockId,SHGSI_ICONLOCATION,&expected)),"system icon reference");
        for(UINT options:{0u,UINT(GIL_OPENICON)}){
            wchar_t path[MAX_PATH]{};int index=0;UINT flags=0;
            Check(SUCCEEDED(icon.value->GetIconLocation(options,path,MAX_PATH,&index,&flags)),"icon location");
            Check(_wcsicmp(path,expected.szPath)==0&&index==expected.iIcon,"icon points only at expected system resource");
        }
        proof::Com<IExtractIconA> ansi;Check(SUCCEEDED(root.value->GetUIObjectOf(nullptr,1,&raw,IID_IExtractIconA,nullptr,reinterpret_cast<void**>(&ansi.value))),"ANSI icon interface");
    }
    puts("stock_icons=passed; kinds=6; asset_paths=0");
}
void ReentrantRelease(proof::Library& library,bool duringQuery){
    auto root=library.Root();snapshot::Entry entry{{301},{302},snapshot::Kind::Directory,L"reentrant"};
    proof::Item item(snapshot::MakePidl(entry));PCUITEMID_CHILD raw=item.value;proof::Com<IContextMenu> context;
    Check(SUCCEEDED(root.value->GetUIObjectOf(nullptr,1,&raw,IID_IContextMenu,nullptr,reinterpret_cast<void**>(&context.value))),"reentrant menu acquisition");
    proof::Com<IObjectWithSite> site;Check(SUCCEEDED(context.value->QueryInterface(IID_PPV_ARGS(&site.value))),"reentrant site interface");
    proof::Com<Browser> browser;browser.value=new Browser();
    Check(SUCCEEDED(site.value->SetSite(static_cast<IShellBrowser*>(browser.value))),"reentrant browser site");
    root.value->Release();root.value=nullptr;
    bool released=false;
    auto release=[&]{
        context.value->Release();context.value=nullptr;site.value->Release();site.value=nullptr;released=true;
        Check(library.canUnload()==S_FALSE,"call-out must retain menu, target and DLL owner");
    };
    if(duringQuery)browser.value->duringQuery=release;else browser.value->duringBrowse=release;
    CMINVOKECOMMANDINFO info{};info.cbSize=sizeof(info);info.lpVerb="open";
    auto caller=context.value;Check(caller->InvokeCommand(&info)==S_OK,"invoke survives release of every external menu reference");
    Check(released&&!context.value&&!site.value&&browser.value->calls==1&&ILIsEqual(browser.value->last,item.value),"reentrant navigation preserves owned target");
    Check(browser.value->refs==1&&library.canUnload()==S_OK,"self/site/owner references released after call-out");
}
}
int wmain(int argc,wchar_t** argv){
    if(argc!=2)return 2;if(FAILED(CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED)))return 1;
    int result=0;
    try {
        proof::Library library(argv[1]);
        for(auto kind:{snapshot::Kind::Library,snapshot::Kind::Directory,snapshot::Kind::NextPage}){
            OpenKind(library,kind);Check(library.canUnload()==S_OK,"all menu/owner/site references released");
        }
        NegativeKinds(library);Icons(library);Check(library.canUnload()==S_OK,"negative/icon cases release references");
        ReentrantRelease(library,true);ReentrantRelease(library,false);
        puts("navigation_menu=passed; kinds=3; default_invoke=numeric_ANSI_Unicode; denied_write_verbs=passed; site_lifetime=passed; registry=0; GUI=0; pipe=0");
    }catch(const std::exception& error){fprintf(stderr,"NavigationMenuTests: %s\n",error.what());result=1;}
    CoUninitialize();return result;
}
