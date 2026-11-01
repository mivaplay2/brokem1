#pragma once

#include <windows.h>

#include <cstddef>
#include <cstdint>
#include <optional>
#include <string>
#include <type_traits>
#include <unordered_map>

// Read-only external access to the local client.
//
// The handle is opened with query + read rights only. There is deliberately no
// write path in this class: no VM_WRITE right is requested and
// WriteProcessMemory is never called.
class Process {
public:
    Process() = default;
    ~Process();

    Process(const Process&) = delete;
    Process& operator=(const Process&) = delete;

    bool Attach(const wchar_t* exe_name);
    void Detach();

    bool IsAttached() const noexcept { return handle_ != nullptr; }
    bool IsAlive() const noexcept;
    DWORD Pid() const noexcept { return pid_; }

    // Resolves and caches a loaded module base. Returns 0 when the module is
    // not loaded yet; the cache is dropped on Detach so a restarted client
    // re-resolves its modules.
    std::uintptr_t ModuleBase(const wchar_t* module_name);

    // Largest visible top-level window owned by the attached process.
    HWND MainWindow() const;

    bool ReadRaw(std::uintptr_t address, void* buffer, std::size_t size) const;

    template <typename T>
    std::optional<T> Read(std::uintptr_t address) const {
        static_assert(std::is_trivially_copyable_v<T>, "Read<T> requires a trivially copyable T.");
        T value{};
        if (!ReadRaw(address, &value, sizeof(T))) {
            return std::nullopt;
        }
        return value;
    }

    template <typename T>
    T ReadOr(std::uintptr_t address, T fallback) const {
        const std::optional<T> value = Read<T>(address);
        return value ? *value : fallback;
    }

    // Returns 0 for failed reads and for pointers outside the plausible
    // user-mode range, so callers only have to test for 0.
    std::uintptr_t ReadPointer(std::uintptr_t address) const;

    // Reads a NUL-terminated ASCII string, replacing non-printable bytes.
    // Always NUL-terminates. Returns false on a failed read.
    bool ReadCString(std::uintptr_t address, char* out, std::size_t capacity) const;

    static bool IsPlausible(std::uintptr_t address) noexcept;

private:
    static DWORD FindPid(const wchar_t* exe_name);

    HANDLE handle_{nullptr};
    DWORD pid_{0};
    std::unordered_map<std::wstring, std::uintptr_t> modules_;
};
