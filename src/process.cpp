#include "process.hpp"

#include <tlhelp32.h>

#include <algorithm>
#include <cctype>
#include <cstring>
#include <cwchar>

namespace {

constexpr std::uintptr_t kMinUserAddress = 0x10000ULL;
constexpr std::uintptr_t kMaxUserAddress = 0x7FFFFFFFFFFFULL;

struct WindowSearch {
    DWORD pid{0};
    HWND best{nullptr};
    long best_area{0};
};

BOOL CALLBACK EnumWindowsProc(HWND hwnd, LPARAM param) {
    auto* search = reinterpret_cast<WindowSearch*>(param);

    DWORD window_pid = 0;
    GetWindowThreadProcessId(hwnd, &window_pid);
    if (window_pid != search->pid) {
        return TRUE;
    }
    if (!IsWindowVisible(hwnd)) {
        return TRUE;
    }

    RECT rect{};
    if (!GetClientRect(hwnd, &rect)) {
        return TRUE;
    }

    const long area = (rect.right - rect.left) * (rect.bottom - rect.top);
    if (area > search->best_area) {
        search->best_area = area;
        search->best = hwnd;
    }
    return TRUE;
}

}  // namespace

Process::~Process() {
    Detach();
}

bool Process::IsPlausible(std::uintptr_t address) noexcept {
    return address >= kMinUserAddress && address < kMaxUserAddress;
}

DWORD Process::FindPid(const wchar_t* exe_name) {
    const HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshot == INVALID_HANDLE_VALUE) {
        return 0;
    }

    PROCESSENTRY32W entry{};
    entry.dwSize = sizeof(entry);

    DWORD pid = 0;
    if (Process32FirstW(snapshot, &entry)) {
        do {
            if (_wcsicmp(entry.szExeFile, exe_name) == 0) {
                pid = entry.th32ProcessID;
                break;
            }
        } while (Process32NextW(snapshot, &entry));
    }

    CloseHandle(snapshot);
    return pid;
}

bool Process::Attach(const wchar_t* exe_name) {
    Detach();

    const DWORD pid = FindPid(exe_name);
    if (pid == 0) {
        return false;
    }

    const HANDLE handle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, FALSE, pid);
    if (handle == nullptr) {
        return false;
    }

    handle_ = handle;
    pid_ = pid;
    return true;
}

void Process::Detach() {
    if (handle_ != nullptr) {
        CloseHandle(handle_);
        handle_ = nullptr;
    }
    pid_ = 0;
    modules_.clear();
}

bool Process::IsAlive() const noexcept {
    if (handle_ == nullptr) {
        return false;
    }
    return WaitForSingleObject(handle_, 0) == WAIT_TIMEOUT;
}

std::uintptr_t Process::ModuleBase(const wchar_t* module_name) {
    if (handle_ == nullptr || module_name == nullptr) {
        return 0;
    }

    const std::wstring key(module_name);
    const auto cached = modules_.find(key);
    if (cached != modules_.end()) {
        return cached->second;
    }

    HANDLE snapshot = INVALID_HANDLE_VALUE;
    for (int attempt = 0; attempt < 8; ++attempt) {
        snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid_);
        if (snapshot != INVALID_HANDLE_VALUE) {
            break;
        }
        if (GetLastError() != ERROR_BAD_LENGTH) {
            return 0;
        }
        Sleep(20);
    }
    if (snapshot == INVALID_HANDLE_VALUE) {
        return 0;
    }

    MODULEENTRY32W entry{};
    entry.dwSize = sizeof(entry);

    std::uintptr_t base = 0;
    if (Module32FirstW(snapshot, &entry)) {
        do {
            if (_wcsicmp(entry.szModule, module_name) == 0) {
                base = reinterpret_cast<std::uintptr_t>(entry.modBaseAddr);
                break;
            }
        } while (Module32NextW(snapshot, &entry));
    }

    CloseHandle(snapshot);

    if (base != 0) {
        modules_.emplace(key, base);
    }
    return base;
}

HWND Process::MainWindow() const {
    if (pid_ == 0) {
        return nullptr;
    }

    WindowSearch search{};
    search.pid = pid_;
    EnumWindows(&EnumWindowsProc, reinterpret_cast<LPARAM>(&search));
    return search.best;
}

bool Process::ReadRaw(std::uintptr_t address, void* buffer, std::size_t size) const {
    if (handle_ == nullptr || buffer == nullptr || size == 0) {
        return false;
    }
    if (!IsPlausible(address)) {
        return false;
    }

    SIZE_T read = 0;
    if (ReadProcessMemory(handle_, reinterpret_cast<LPCVOID>(address), buffer, size, &read) == 0) {
        return false;
    }
    return read == size;
}

std::uintptr_t Process::ReadPointer(std::uintptr_t address) const {
    const std::optional<std::uintptr_t> value = Read<std::uintptr_t>(address);
    if (!value || !IsPlausible(*value)) {
        return 0;
    }
    return *value;
}

bool Process::ReadCString(std::uintptr_t address, char* out, std::size_t capacity) const {
    if (out == nullptr || capacity == 0) {
        return false;
    }
    out[0] = '\0';

    if (!ReadRaw(address, out, capacity)) {
        return false;
    }

    out[capacity - 1] = '\0';
    for (std::size_t i = 0; i < capacity; ++i) {
        if (out[i] == '\0') {
            break;
        }
        const auto byte = static_cast<unsigned char>(out[i]);
        if (byte < 0x20 || byte > 0x7E) {
            out[i] = '?';
        }
    }
    return true;
}
