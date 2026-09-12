#include "Snapshot.h"
#include <objbase.h>
#include <sddl.h>
#include <algorithm>
#include <cstdio>
#include <cstring>
#include <stdexcept>
#include <thread>

namespace {
constexpr DWORD CrashExit = 0xe0000012, CleanupExit = 70;
struct Handle {
    HANDLE value = nullptr;
    explicit Handle(HANDLE handle = nullptr) : value(handle) {}
    ~Handle() { if (value && value != INVALID_HANDLE_VALUE) CloseHandle(value); }
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
};
enum class Mode { Silent, InvalidVersion, PartialFrame, Crash, Ready };
struct Options {
    Mode mode = Mode::Silent;
    bool execute = false;
    DWORD lifetime = 30000, connections = 32;
};
const char* Name(Mode mode) {
    switch (mode) {
    case Mode::InvalidVersion: return "invalid-version";
    case Mode::PartialFrame: return "partial-frame";
    case Mode::Crash: return "crash";
    case Mode::Ready: return "ready";
    default: return "silent";
    }
}
DWORD Number(const wchar_t* text, DWORD maximum) {
    DWORD value = 0;
    if (!*text) throw std::invalid_argument("number");
    for (; *text; ++text) {
        if (*text < L'0' || *text > L'9' || value > maximum / 10) throw std::invalid_argument("number");
        value = value * 10 + static_cast<DWORD>(*text - L'0');
        if (value > maximum) throw std::invalid_argument("number");
    }
    if (!value) throw std::invalid_argument("number");
    return value;
}
Options Parse(int argc, wchar_t** argv) {
    Options options;
    bool modeSet = false;
    for (int i = 1; i < argc; ++i) {
        const std::wstring arg = argv[i];
        if (arg == L"--execute") options.execute = true;
        else if (i + 1 < argc && arg == L"--mode") {
            const std::wstring mode = argv[++i];
            if (mode == L"silent") options.mode = Mode::Silent;
            else if (mode == L"invalid-version") options.mode = Mode::InvalidVersion;
            else if (mode == L"partial-frame") options.mode = Mode::PartialFrame;
            else if (mode == L"crash") options.mode = Mode::Crash;
            else if (mode == L"ready") options.mode = Mode::Ready;
            else throw std::invalid_argument("mode");
            modeSet = true;
        } else if (i + 1 < argc && arg == L"--lifetime-ms") options.lifetime = Number(argv[++i], 119000);
        else if (i + 1 < argc && arg == L"--max-connections") options.connections = Number(argv[++i], 128);
        else throw std::invalid_argument("argument");
    }
    if (options.execute && !modeSet) throw std::invalid_argument("explicit mode required");
    return options;
}
struct Log {
    Mode mode;
    ULONGLONG started = GetTickCount64();
    DWORD requests = 0;
    ULONG clientPid = 0;
    void Event(const char* event, DWORD responseBytes = 0) const {
        printf("{\"mode\":\"%s\",\"pid\":%lu,\"client_pid\":%lu,\"requests\":%lu,\"elapsed_ms\":%llu,\"response_bytes\":%lu,\"event\":\"%s\"}\n",
            Name(mode), GetCurrentProcessId(), clientPid, requests, GetTickCount64() - started, responseBytes, event);
    }
    [[noreturn]] void Exit(DWORD code, const char* event) const {
        Event(event);
        // Only this explicitly armed fault process is terminated, without a WER/UI dialog.
        TerminateProcess(GetCurrentProcess(), code);
        ExitProcess(code);
    }
};
struct Watchdog {
    Handle done{CreateEventW(nullptr, TRUE, FALSE, nullptr)};
    std::thread thread;
    explicit Watchdog(DWORD lifetime) {
        if (!done.value) throw std::runtime_error("watchdog event");
        // Also bound a blocked output consumer or a stalled OS cleanup. The extra second
        // is reserved for cancellation; 119000 + 1000 never exceeds the 120-second cap.
        thread = std::thread([this, lifetime] {
            if (WaitForSingleObject(done.value, lifetime + 1000) != WAIT_OBJECT_0)
                TerminateProcess(GetCurrentProcess(), CleanupExit);
        });
    }
    ~Watchdog() { SetEvent(done.value); thread.join(); }
    Watchdog(const Watchdog&) = delete;
    Watchdog& operator=(const Watchdog&) = delete;
};
struct Security {
    PSECURITY_DESCRIPTOR descriptor = nullptr;
    SECURITY_ATTRIBUTES attributes{sizeof(SECURITY_ATTRIBUTES), nullptr, FALSE};
    Security() {
        HANDLE raw = nullptr;
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &raw)) throw std::runtime_error("token");
        Handle token(raw);
        DWORD size = 0;
        GetTokenInformation(token.value, TokenUser, nullptr, 0, &size);
        if (GetLastError() != ERROR_INSUFFICIENT_BUFFER || size < sizeof(TOKEN_USER) || size > 4096) throw std::runtime_error("token size");
        std::vector<BYTE> bytes(size);
        if (!GetTokenInformation(token.value, TokenUser, bytes.data(), size, &size)) throw std::runtime_error("user");
        const auto sid = reinterpret_cast<TOKEN_USER*>(bytes.data())->User.Sid;
        LPWSTR text = nullptr;
        if (!IsValidSid(sid) || !ConvertSidToStringSidW(sid, &text)) throw std::runtime_error("SID");
        std::wstring sddl;
        try { sddl = L"D:P(A;;GA;;;" + std::wstring(text) + L")"; }
        catch (...) { LocalFree(text); throw; }
        LocalFree(text);
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl.c_str(), SDDL_REVISION_1, &descriptor, nullptr)) throw std::runtime_error("DACL");
        attributes.lpSecurityDescriptor = descriptor;
    }
    ~Security() { if (descriptor) LocalFree(descriptor); }
    Security(const Security&) = delete;
    Security& operator=(const Security&) = delete;
};
DWORD Remaining(ULONGLONG end) {
    const auto now = GetTickCount64();
    return now >= end ? 0 : static_cast<DWORD>(end - now);
}
// One process-owned operation at a time. Never release OVERLAPPED storage before completion.
bool Complete(HANDLE pipe, OVERLAPPED& operation, HANDLE stop, ULONGLONG end, DWORD& transferred, const Log& log) {
    const HANDLE waits[]{stop, operation.hEvent};
    if (WaitForMultipleObjects(2, waits, FALSE, Remaining(end)) == WAIT_OBJECT_0 + 1)
        return GetOverlappedResult(pipe, &operation, &transferred, FALSE) != FALSE;
    CancelIoEx(pipe, &operation);
    if (WaitForSingleObject(operation.hEvent, 1000) != WAIT_OBJECT_0) log.Exit(CleanupExit, "cleanup_timeout");
    return false;
}
bool Transfer(HANDLE pipe, HANDLE stop, bool write, BYTE* bytes, DWORD length, ULONGLONG end, const Log& log) {
    Handle event(CreateEventW(nullptr, TRUE, FALSE, nullptr));
    if (!event.value) throw std::runtime_error("I/O event");
    OVERLAPPED operation{};
    operation.hEvent = event.value;
    DWORD at = 0;
    while (at < length && Remaining(end) && WaitForSingleObject(stop, 0) == WAIT_TIMEOUT) {
        ResetEvent(event.value);
        DWORD transferred = 0;
        const BOOL done = write ? WriteFile(pipe, bytes + at, length - at, &transferred, &operation)
                                : ReadFile(pipe, bytes + at, length - at, &transferred, &operation);
        if (!done && (GetLastError() != ERROR_IO_PENDING || !Complete(pipe, operation, stop, end, transferred, log))) return false;
        if (!transferred || transferred > length - at) return false;
        at += transferred;
    }
    return at == length;
}
bool Connect(HANDLE pipe, HANDLE stop, ULONGLONG end, const Log& log) {
    Handle event(CreateEventW(nullptr, TRUE, FALSE, nullptr));
    if (!event.value) throw std::runtime_error("connect event");
    OVERLAPPED operation{};
    operation.hEvent = event.value;
    const BOOL connected = ConnectNamedPipe(pipe, &operation);
    const DWORD error = connected ? ERROR_SUCCESS : GetLastError();
    if (!connected && error != ERROR_PIPE_CONNECTED && error != ERROR_IO_PENDING) throw std::runtime_error("connect");
    log.Event("listening");
    if (error != ERROR_IO_PENDING) return true;
    DWORD transferred = 0;
    return Complete(pipe, operation, stop, end, transferred, log);
}
ULONG RequestId(const std::array<BYTE, 48>& bytes) {
    ULONG id = 0;
    snapshot::Location location;
    std::memcpy(&id, bytes.data() + 12, sizeof(id));
    std::memcpy(&location, bytes.data() + 16, sizeof(location));
    try { return snapshot::Request(location, id) == bytes ? id : 0; }
    catch (const std::invalid_argument&) { return 0; }
}
std::array<BYTE, 40> Frame(ULONG id, Mode mode, const GUID& epoch) {
    std::array<BYTE, 40> frame{0x41, 0x4c, 0x53, 0x31, 1, 0, 2, 0, 24};
    std::memcpy(frame.data() + 12, &id, sizeof(id));
    std::memcpy(frame.data() + 20, &epoch, sizeof(epoch));
    if (mode == Mode::InvalidVersion) frame[4] = 2;
    return frame;
}
int Run(const Options& options, Log& log) {
    Security security;
    const auto eventName = L"Local\\AssetLibrary.ExplorerFault.Stop." + std::to_wstring(GetCurrentProcessId());
    Handle stop(CreateEventW(&security.attributes, TRUE, FALSE, eventName.c_str()));
    if (!stop.value || GetLastError() == ERROR_ALREADY_EXISTS) throw std::runtime_error("stop event");
    const auto name = snapshot::PipeName();
    if (name.empty()) throw std::runtime_error("endpoint");
    Handle pipe(CreateNamedPipeW(name.c_str(), PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED | FILE_FLAG_FIRST_PIPE_INSTANCE,
        PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
        1, 4096, 4096, 0, &security.attributes));
    if (pipe.value == INVALID_HANDLE_VALUE) { log.Event("pipe_unavailable"); return 3; }
    DWORD ownSession = 0;
    GUID epoch{};
    if (!ProcessIdToSessionId(GetCurrentProcessId(), &ownSession) || FAILED(CoCreateGuid(&epoch))) throw std::runtime_error("identity");
    const auto end = log.started + options.lifetime;
    DWORD connections = 0;
    while (connections < options.connections && Remaining(end) && WaitForSingleObject(stop.value, 0) == WAIT_TIMEOUT) {
        log.clientPid = 0;
        if (!Connect(pipe.value, stop.value, end, log)) break;
        ++connections;
        const auto connectionEnd = (std::min)(end, GetTickCount64() + 500);
        ULONG session = 0;
        std::array<BYTE, 48> request{};
        if (!GetNamedPipeClientSessionId(pipe.value, &session) || session != ownSession ||
            !GetNamedPipeClientProcessId(pipe.value, &log.clientPid) || !log.clientPid) log.Event("client_rejected");
        else if (Transfer(pipe.value, stop.value, false, request.data(), static_cast<DWORD>(request.size()), connectionEnd, log)) {
            const auto id = RequestId(request);
            if (!id) log.Event("request_rejected");
            else {
                ++log.requests;
                log.Event("request_received");
                if (options.mode == Mode::Crash) log.Exit(CrashExit, "expected_crash");
                if (options.mode != Mode::Silent) {
                    auto frame = Frame(id, options.mode, epoch);
                    // The positive control only serves an empty synthetic root; it owns no node mapping.
                    if (options.mode == Mode::Ready && std::any_of(request.begin() + 16, request.begin() + 32,
                        [](BYTE value) { return value != 0; })) frame[16] = static_cast<BYTE>(snapshot::Status::Expired);
                    const DWORD responseBytes = options.mode == Mode::PartialFrame ? 20 : 40;
                    if (Transfer(pipe.value, stop.value, true, frame.data(), responseBytes, connectionEnd, log))
                        log.Event("response_written", responseBytes);
                    else log.Event("response_failed");
                }
                // Disconnect discards buffered bytes, so permit bounded client-close observation.
                BYTE ignored = 0;
                Transfer(pipe.value, stop.value, false, &ignored, 1, connectionEnd, log);
            }
        }
        DisconnectNamedPipe(pipe.value);
    }
    // Close the endpoint before publishing the terminal event.
    CloseHandle(pipe.value);
    pipe.value = nullptr;
    log.clientPid = 0;
    const bool stopped = WaitForSingleObject(stop.value, 0) == WAIT_OBJECT_0;
    const bool expected = stopped || !Remaining(end) || connections >= options.connections;
    log.Event(stopped ? "stopped" : !Remaining(end) ? "deadline" : connections >= options.connections ? "connection_limit" : "io_failed");
    return expected ? 0 : 1;
}
}
int wmain(int argc, wchar_t** argv) {
    setvbuf(stdout, nullptr, _IONBF, 0);
    Options options;
    try { options = Parse(argc, argv); }
    catch (const std::invalid_argument&) {
        fputs("Usage: ExplorerFaultHost [--execute --mode silent|invalid-version|partial-frame|crash|ready] "
              "[--lifetime-ms 1..119000] [--max-connections 1..128]\n", stderr);
        return 2;
    }
    Log log{options.mode};
    if (!options.execute) { log.Event("dry_run"); return 0; }
    try {
        Watchdog watchdog(options.lifetime);
        try { return Run(options, log); }
        catch (const std::exception&) { log.Event("failed"); return 1; }
    } catch (const std::exception&) { return 1; }
}
