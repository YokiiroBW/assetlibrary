#include "Snapshot.h"
#include <aclapi.h>
#include <algorithm>
#include <cstdio>
#include <stdexcept>
#include <string>

namespace {
void Check(bool condition, const char* message) { if (!condition) throw std::runtime_error(message); }
struct Handle {
    HANDLE value = nullptr;
    explicit Handle(HANDLE handle = nullptr) : value(handle) {}
    ~Handle() { if (value && value != INVALID_HANDLE_VALUE) CloseHandle(value); }
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
};
class Child {
    Handle process_, output_;
    DWORD pid_ = 0;
public:
    std::string log;
    Child(const wchar_t* executable, const std::wstring& arguments) {
        SECURITY_ATTRIBUTES security{sizeof(security), nullptr, TRUE};
        HANDLE reader = nullptr, writer = nullptr;
        Check(CreatePipe(&reader, &writer, &security, 4096) != FALSE, "output pipe");
        output_.value = reader;
        Handle output(writer);
        Check(SetHandleInformation(reader, HANDLE_FLAG_INHERIT, 0) != FALSE, "private output reader");
        Handle input(CreateFileW(L"NUL", GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, &security, OPEN_EXISTING, 0, nullptr));
        Check(input.value != INVALID_HANDLE_VALUE, "null input");
        STARTUPINFOW startup{sizeof(startup)};
        startup.dwFlags = STARTF_USESTDHANDLES;
        startup.hStdInput = input.value;
        startup.hStdOutput = output.value;
        startup.hStdError = output.value;
        PROCESS_INFORMATION information{};
        auto command = L"\"" + std::wstring(executable) + L"\" " + arguments;
        Check(CreateProcessW(executable, command.data(), nullptr, nullptr, TRUE, CREATE_NO_WINDOW, nullptr, nullptr,
            &startup, &information) != FALSE, "spawn actual fault CLI");
        process_.value = information.hProcess;
        pid_ = information.dwProcessId;
        CloseHandle(information.hThread);
    }
    ~Child() {
        if (WaitForSingleObject(process_.value, 0) == WAIT_TIMEOUT) {
            SignalStop();
            if (WaitForSingleObject(process_.value, 2000) != WAIT_OBJECT_0) {
                // Failure cleanup owns only the process created by this test.
                TerminateProcess(process_.value, 71);
                WaitForSingleObject(process_.value, 1000);
            }
        }
    }
    void Read() {
        DWORD available = 0;
        while (PeekNamedPipe(output_.value, nullptr, 0, nullptr, &available, nullptr) && available) {
            char bytes[1024];
            DWORD received = 0;
            Check(ReadFile(output_.value, bytes, (std::min)(available, static_cast<DWORD>(sizeof(bytes))), &received, nullptr) != FALSE, "read log");
            Check(received && log.size() + received < 65536, "bounded log");
            log.append(bytes, received);
        }
    }
    void Event(const char* name, unsigned occurrence = 1) {
        const auto marker = "\"event\":\"" + std::string(name) + "\"";
        const auto end = GetTickCount64() + 2000;
        while (GetTickCount64() < end) {
            Read();
            size_t at = 0;
            unsigned found = 0;
            while ((at = log.find(marker, at)) != std::string::npos) { ++found; at += marker.size(); }
            if (found >= occurrence) return;
            Check(WaitForSingleObject(process_.value, 0) == WAIT_TIMEOUT, "CLI exited before expected event");
            Sleep(10);
        }
        throw std::runtime_error("CLI event timeout");
    }
    bool SignalStop() const {
        const auto name = L"Local\\AssetLibrary.ExplorerFault.Stop." + std::to_wstring(pid_);
        Handle stop(OpenEventW(EVENT_MODIFY_STATE, FALSE, name.c_str()));
        return stop.value && SetEvent(stop.value);
    }
    void Exit(DWORD expected, const char* event) {
        const auto end = GetTickCount64() + 2500;
        while (GetTickCount64() < end && WaitForSingleObject(process_.value, 10) == WAIT_TIMEOUT) Read();
        DWORD code = STILL_ACTIVE;
        Check(GetExitCodeProcess(process_.value, &code) && code == expected, "CLI exit code");
        Read();
        if (event) Check(log.find("\"event\":\"" + std::string(event) + "\"") != std::string::npos, "terminal JSON event");
    }
    void Stop() { Check(SignalStop(), "owned stop event"); Exit(0, "stopped"); }
    Child(const Child&) = delete;
    Child& operator=(const Child&) = delete;
};
void Reaped() {
    const auto end = GetTickCount64() + 1000;
    while (snapshot::PendingOperations() && GetTickCount64() < end) Sleep(5);
    Check(!snapshot::PendingOperations(), "client operations reaped");
}
void Query(snapshot::Status expected, bool timeout = false) {
    const auto start = GetTickCount64();
    const auto page = snapshot::Query({});
    const auto elapsed = GetTickCount64() - start;
    printf("client_status=%lu elapsed_ms=%llu\n", static_cast<ULONG>(page.status), elapsed);
    Check(page.status == expected && page.entries.empty(), "existing Shell client status");
    Check(elapsed < 300 && (!timeout || elapsed >= 120), "150ms foreground budget with scheduling tolerance");
    Reaped();
}
std::wstring Armed(const wchar_t* mode, const wchar_t* extra = L"") {
    return L"--execute --mode " + std::wstring(mode) + L" --lifetime-ms 5000 " + extra;
}
void Recovery(const wchar_t* executable) {
    Child host(executable, Armed(L"ready"));
    host.Event("listening");
    Query(snapshot::Status::Ready);
    host.Stop();
    Query(snapshot::Status::Unavailable);
}
void ActualDacl(HANDLE pipe) {
    PSECURITY_DESCRIPTOR descriptor = nullptr;
    PACL dacl = nullptr;
    Check(GetSecurityInfo(pipe, SE_KERNEL_OBJECT, DACL_SECURITY_INFORMATION, nullptr, nullptr, &dacl, nullptr, &descriptor) == ERROR_SUCCESS, "actual pipe DACL");
    HANDLE raw = nullptr;
    const bool opened = OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &raw) != FALSE;
    Handle token(raw);
    DWORD size = 0;
    if (opened) GetTokenInformation(token.value, TokenUser, nullptr, 0, &size);
    std::vector<BYTE> bytes(size);
    bool valid = opened && size && GetTokenInformation(token.value, TokenUser, bytes.data(), size, &size);
    void* rawAce = nullptr;
    valid = valid && dacl && dacl->AceCount == 1 && GetAce(dacl, 0, &rawAce);
    if (valid) {
        const auto ace = static_cast<ACCESS_ALLOWED_ACE*>(rawAce);
        valid = ace->Header.AceType == ACCESS_ALLOWED_ACE_TYPE &&
            EqualSid(&ace->SidStart, reinterpret_cast<TOKEN_USER*>(bytes.data())->User.Sid);
    }
    LocalFree(descriptor);
    Check(valid, "DACL grants only actual TokenUser SID");
}
void Tests(const wchar_t* executable) {
    { Child lazy(executable, L"--mode crash"); lazy.Exit(0, "dry_run"); }
    for (const auto* arguments : {L"--execute", L"--mode unknown", L"--lifetime-ms 119001", L"--lifetime-ms 0", L"--max-connections 129"}) {
        Child rejected(executable, arguments); rejected.Exit(2, nullptr);
    }
    {
        Child first(executable, Armed(L"ready")); first.Event("listening");
        { Child second(executable, Armed(L"crash")); second.Exit(3, "pipe_unavailable"); }
        { Child lazy(executable, L"--mode crash"); lazy.Exit(0, "dry_run"); }
        Query(snapshot::Status::Ready);
        first.Event("listening", 2);
        Query(snapshot::Status::Ready);
        first.Stop();
    }
    for (const auto* mode : {L"silent", L"invalid-version", L"partial-frame"}) {
        {
            Child host(executable, Armed(mode)); host.Event("listening");
            const bool invalid = std::wstring(mode) == L"invalid-version";
            Query(invalid ? snapshot::Status::InvalidResponse : snapshot::Status::Unavailable, !invalid);
            host.Event("request_received");
            if (std::wstring(mode) != L"silent") {
                host.Event("response_written");
                const auto marker = "\"response_bytes\":" + std::to_string(invalid ? 40 : 20) + ",\"event\":\"response_written\"";
                Check(host.log.find(marker) != std::string::npos, "fault frame bytes actually written, not silent fallback");
            }
            host.Stop();
        }
        Recovery(executable);
    }
    {
        Child crash(executable, Armed(L"crash")); crash.Event("listening");
        Query(snapshot::Status::Unavailable);
        crash.Exit(0xe0000012, "expected_crash");
        Check(crash.log.find("\"requests\":1") != std::string::npos, "crash occurs after a valid actual request");
        Check(crash.log.find("\"client_pid\":" + std::to_string(GetCurrentProcessId()) + ",") != std::string::npos, "actual requesting PID is logged");
    }
    Recovery(executable);
    {
        Child expiry(executable, L"--execute --mode silent --lifetime-ms 250");
        expiry.Event("listening"); expiry.Exit(0, "deadline");
    }
    Recovery(executable);
    {
        Child limited(executable, Armed(L"ready", L"--max-connections 1")); limited.Event("listening");
        Query(snapshot::Status::Ready); limited.Exit(0, "connection_limit");
    }
    Recovery(executable);
    {
        Child partialRequest(executable, Armed(L"silent")); partialRequest.Event("listening");
        const auto endpoint = snapshot::PipeName();
        Handle client(CreateFileW(endpoint.c_str(), GENERIC_READ | GENERIC_WRITE | READ_CONTROL, 0, nullptr, OPEN_EXISTING,
            SECURITY_SQOS_PRESENT | SECURITY_IDENTIFICATION, nullptr));
        Check(client.value != INVALID_HANDLE_VALUE, "connect without a complete request");
        ActualDacl(client.value);
        // Stop must cancel a pending request read, not wait for the client to send or close.
        Sleep(30); partialRequest.Stop();
    }
    Recovery(executable);
    puts("fault_harness=passed; actual_CLI=passed; states_deadlines_stop_exclusion_reconnect=passed; actual_Explorer=not_tested");
}
}
int wmain(int argc, wchar_t** argv) {
    setvbuf(stdout, nullptr, _IONBF, 0);
    if (argc != 2) return 2;
    try { Tests(argv[1]); return 0; }
    catch (const std::exception& error) { fprintf(stderr, "FaultHarnessTests: %s\n", error.what()); return 1; }
}
