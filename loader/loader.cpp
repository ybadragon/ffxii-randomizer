// FFXII randomizer loader.
//
// Built as dinput8.dll and dropped next to FFXII_TZA.exe. The game imports DirectInput8Create from
// dinput8.dll, so Windows loads this copy first; we forward that one export to the real system DLL.
//
// On load we patch the game's import table for CreateFileA, ReadFile, SetFilePointer and
// CloseHandle. When the game opens FFXII_TZA.vbf and a seed patch exists, reads of that handle are
// served from a *virtual* archive:
//
//   [0, H')                 our rewritten header (from the patch file)
//   [H', realSize + delta)  the real archive's data, i.e. real offset = virtual offset - delta
//   [realSize + delta, ..)  new file data appended by the patch
//
// The real archive is never written. With no patch file present the loader does nothing.

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <objbase.h>
#include <algorithm>
#include <cstdarg>
#include <cstdint>
#include <cstdio>
#include <share.h>
#include <cstring>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>

namespace
{
#pragma pack(push, 1)
struct PatchHeader
{
    char magic[4];             // "F12P"
    uint32_t version;          // 1
    uint64_t realHeaderLength; // header length of the untouched archive
    uint64_t virtualHeaderLength;
    uint64_t realFileSize;
    uint64_t appendedVirtualOffset;
    uint64_t appendedLength;
    uint64_t headerBlobPos;    // within the patch file
    uint64_t appendedBlobPos;  // within the patch file
};
#pragma pack(pop)

using CreateFileA_t = HANDLE(WINAPI*)(LPCSTR, DWORD, DWORD, LPSECURITY_ATTRIBUTES, DWORD, DWORD, HANDLE);
using ReadFile_t = BOOL(WINAPI*)(HANDLE, LPVOID, DWORD, LPDWORD, LPOVERLAPPED);
using SetFilePointer_t = DWORD(WINAPI*)(HANDLE, LONG, PLONG, DWORD);
using CloseHandle_t = BOOL(WINAPI*)(HANDLE);

CreateFileA_t realCreateFileA = CreateFileA;
ReadFile_t realReadFile = ReadFile;
SetFilePointer_t realSetFilePointer = SetFilePointer;
CloseHandle_t realCloseHandle = CloseHandle;

std::wstring gameDir;   // folder holding FFXII_TZA.vbf (parent of x64)
std::wstring randoDir;  // gameDir\ff12rando
FILE* logFile = nullptr;
std::mutex lock;

bool patchLoaded = false;
bool verbose = false; // verbose.txt in the ff12rando folder: log every archive read
PatchHeader patch{};
std::vector<uint8_t> virtualHeader;
HANDLE patchFile = INVALID_HANDLE_VALUE;

struct VirtualHandle
{
    uint64_t position = 0;
    uint64_t reads = 0;
};
std::unordered_map<HANDLE, VirtualHandle> vbfHandles;

void Log(const char* format, ...)
{
    if (!logFile)
    {
        return;
    }
    va_list args;
    va_start(args, format);
    vfprintf(logFile, format, args);
    va_end(args);
    fputc('\n', logFile);
    fflush(logFile);
}

bool EndsWithInsensitive(const char* text, const char* suffix)
{
    size_t a = strlen(text), b = strlen(suffix);
    return a >= b && _stricmp(text + a - b, suffix) == 0;
}

bool ReadAt(HANDLE file, uint64_t offset, void* buffer, DWORD length)
{
    LONG high = static_cast<LONG>(offset >> 32);
    if (realSetFilePointer(file, static_cast<LONG>(offset & 0xFFFFFFFF), &high, FILE_BEGIN) == INVALID_SET_FILE_POINTER
        && GetLastError() != NO_ERROR)
    {
        return false;
    }
    DWORD got = 0;
    return realReadFile(file, buffer, length, &got, nullptr) && got == length;
}

void LoadPatch()
{
    std::wstring path = randoDir + L"\\patch.f12p";
    HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE)
    {
        Log("no patch file, running unmodified");
        return;
    }

    PatchHeader header{};
    DWORD got = 0;
    if (!realReadFile(file, &header, sizeof(header), &got, nullptr) || got != sizeof(header)
        || memcmp(header.magic, "F12P", 4) != 0 || header.version != 1)
    {
        Log("patch file unreadable or wrong version, running unmodified");
        realCloseHandle(file);
        return;
    }

    // Refuse a patch built against a different archive (a game update, or another language pack).
    std::wstring vbfPath = gameDir + L"\\FFXII_TZA.vbf";
    WIN32_FILE_ATTRIBUTE_DATA attributes{};
    if (!GetFileAttributesExW(vbfPath.c_str(), GetFileExInfoStandard, &attributes)
        || ((static_cast<uint64_t>(attributes.nFileSizeHigh) << 32) | attributes.nFileSizeLow) != header.realFileSize)
    {
        Log("archive size does not match the patch, running unmodified");
        realCloseHandle(file);
        return;
    }

    virtualHeader.resize(static_cast<size_t>(header.virtualHeaderLength));
    if (!ReadAt(file, header.headerBlobPos, virtualHeader.data(), static_cast<DWORD>(virtualHeader.size())))
    {
        Log("could not read patch header blob, running unmodified");
        realCloseHandle(file);
        return;
    }

    patch = header;
    patchFile = file;
    patchLoaded = true;
    Log("patch loaded: header %llu -> %llu bytes, %llu bytes of new data",
        header.realHeaderLength, header.virtualHeaderLength, header.appendedLength);
}

// Serves [offset, offset + length) of the virtual archive into buffer.
bool ReadVirtual(HANDLE realHandle, uint64_t offset, uint8_t* buffer, DWORD length, DWORD* produced)
{
    const uint64_t delta = patch.virtualHeaderLength - patch.realHeaderLength;
    const uint64_t realDataEnd = patch.realFileSize + delta;
    const uint64_t end = patch.appendedVirtualOffset + patch.appendedLength;
    *produced = 0;

    while (*produced < length && offset < end)
    {
        uint8_t* out = buffer + *produced;
        DWORD chunk;
        if (offset < patch.virtualHeaderLength)
        {
            chunk = static_cast<DWORD>(std::min<uint64_t>(length - *produced, patch.virtualHeaderLength - offset));
            memcpy(out, virtualHeader.data() + offset, chunk);
        }
        else if (offset < realDataEnd)
        {
            chunk = static_cast<DWORD>(std::min<uint64_t>(length - *produced, realDataEnd - offset));
            if (!ReadAt(realHandle, offset - delta, out, chunk))
            {
                return false;
            }
        }
        else if (offset >= patch.appendedVirtualOffset)
        {
            chunk = static_cast<DWORD>(std::min<uint64_t>(length - *produced, end - offset));
            if (!ReadAt(patchFile, patch.appendedBlobPos + (offset - patch.appendedVirtualOffset), out, chunk))
            {
                return false;
            }
        }
        else
        {
            // Gap between real data and appended data: zero fill.
            chunk = static_cast<DWORD>(std::min<uint64_t>(length - *produced, patch.appendedVirtualOffset - offset));
            memset(out, 0, chunk);
        }
        offset += chunk;
        *produced += chunk;
    }
    return true;
}

HANDLE WINAPI HookCreateFileA(LPCSTR name, DWORD access, DWORD share, LPSECURITY_ATTRIBUTES security, DWORD disposition,
    DWORD flags, HANDLE templateFile)
{
    HANDLE handle = realCreateFileA(name, access, share, security, disposition, flags, templateFile);
    if (name && handle != INVALID_HANDLE_VALUE && EndsWithInsensitive(name, "FFXII_TZA.vbf"))
    {
        std::lock_guard guard(lock);
        Log("game opened %s (flags %08lx)%s", name, flags, patchLoaded ? ", virtualizing" : "");
        if (patchLoaded)
        {
            vbfHandles[handle] = VirtualHandle{};
        }
    }
    return handle;
}

DWORD WINAPI HookSetFilePointer(HANDLE file, LONG distance, PLONG distanceHigh, DWORD method)
{
    {
        std::lock_guard guard(lock);
        auto it = vbfHandles.find(file);
        if (it != vbfHandles.end())
        {
            int64_t move = distanceHigh ? ((static_cast<int64_t>(*distanceHigh) << 32) | static_cast<uint32_t>(distance))
                                        : static_cast<int64_t>(distance);
            uint64_t base = method == FILE_CURRENT ? it->second.position
                : method == FILE_END ? patch.appendedVirtualOffset + patch.appendedLength
                : 0;
            uint64_t position = base + move;
            it->second.position = position;
            if (distanceHigh)
            {
                *distanceHigh = static_cast<LONG>(position >> 32);
            }
            SetLastError(NO_ERROR);
            return static_cast<DWORD>(position & 0xFFFFFFFF);
        }
    }
    return realSetFilePointer(file, distance, distanceHigh, method);
}

BOOL WINAPI HookReadFile(HANDLE file, LPVOID buffer, DWORD length, LPDWORD read, LPOVERLAPPED overlapped)
{
    {
        std::lock_guard guard(lock);
        auto it = vbfHandles.find(file);
        if (it != vbfHandles.end())
        {
            if (overlapped)
            {
                Log("overlapped read on the archive is not supported");
            }
            DWORD produced = 0;
            bool ok = ReadVirtual(file, it->second.position, static_cast<uint8_t*>(buffer), length, &produced);
            if (verbose || it->second.reads++ < 8 || it->second.position >= patch.appendedVirtualOffset)
            {
                Log("read %lu at %llx -> %lu%s", length, it->second.position, produced, ok ? "" : " FAILED");
            }
            it->second.position += produced;
            if (read)
            {
                *read = produced;
            }
            return ok ? TRUE : FALSE;
        }
    }
    return realReadFile(file, buffer, length, read, overlapped);
}

BOOL WINAPI HookCloseHandle(HANDLE handle)
{
    {
        std::lock_guard guard(lock);
        vbfHandles.erase(handle);
    }
    return realCloseHandle(handle);
}

// ---- C runtime stdio hooks ----------------------------------------------------------------------
// The game reads the archive through fopen/fread/_fseeki64, not through Win32 directly. A FILE*
// for the archive is still opened for real (so the game's fclose and error paths behave), but its
// reads and seeks are answered from the virtual archive using our own handle to the real file.

using fopen_t = FILE*(__cdecl*)(const char*, const char*);
using wfopen_t = FILE*(__cdecl*)(const wchar_t*, const wchar_t*);
using wfopen_s_t = errno_t(__cdecl*)(FILE**, const wchar_t*, const wchar_t*);
using fread_t = size_t(__cdecl*)(void*, size_t, size_t, FILE*);
using fseek_t = int(__cdecl*)(FILE*, long, int);
using fseeki64_t = int(__cdecl*)(FILE*, __int64, int);
using ftell_t = long(__cdecl*)(FILE*);
using ftelli64_t = __int64(__cdecl*)(FILE*);
using fgetpos_t = int(__cdecl*)(FILE*, fpos_t*);
using fsetpos_t = int(__cdecl*)(FILE*, const fpos_t*);
using fclose_t = int(__cdecl*)(FILE*);

fopen_t realFopen;
wfopen_t realWfopen;
wfopen_s_t realWfopenS;
fread_t realFread;
fseek_t realFseek;
fseeki64_t realFseeki64;
ftell_t realFtell;
ftelli64_t realFtelli64;
fgetpos_t realFgetpos;
fsetpos_t realFsetpos;
fclose_t realFclose;

HANDLE vbfReal = INVALID_HANDLE_VALUE; // our own read handle on the real archive
std::unordered_map<FILE*, VirtualHandle> vbfFiles;
int opensLogged = 0;

bool EndsWithInsensitiveW(const wchar_t* text, const wchar_t* suffix)
{
    size_t a = wcslen(text), b = wcslen(suffix);
    return a >= b && _wcsicmp(text + a - b, suffix) == 0;
}

void TrackOpen(FILE* file, bool isArchive, const char* narrow, const wchar_t* wide)
{
    std::lock_guard guard(lock);
    if (opensLogged < 40 || isArchive)
    {
        opensLogged++;
        if (narrow)
        {
            Log("fopen %s -> %p", narrow, static_cast<void*>(file));
        }
        else
        {
            Log("wfopen %ls -> %p", wide, static_cast<void*>(file));
        }
    }
    if (file && isArchive && patchLoaded)
    {
        vbfFiles[file] = VirtualHandle{};
        Log("archive FILE %p is now virtual", static_cast<void*>(file));
    }
}

FILE* __cdecl HookFopen(const char* name, const char* mode)
{
    FILE* file = realFopen(name, mode);
    TrackOpen(file, name && EndsWithInsensitive(name, "FFXII_TZA.vbf"), name ? name : "(null)", nullptr);
    return file;
}

FILE* __cdecl HookWfopen(const wchar_t* name, const wchar_t* mode)
{
    FILE* file = realWfopen(name, mode);
    TrackOpen(file, name && EndsWithInsensitiveW(name, L"FFXII_TZA.vbf"), nullptr, name ? name : L"(null)");
    return file;
}

errno_t __cdecl HookWfopenS(FILE** out, const wchar_t* name, const wchar_t* mode)
{
    errno_t result = realWfopenS(out, name, mode);
    TrackOpen(result == 0 && out ? *out : nullptr, name && EndsWithInsensitiveW(name, L"FFXII_TZA.vbf"), nullptr,
        name ? name : L"(null)");
    return result;
}

size_t __cdecl HookFread(void* buffer, size_t size, size_t count, FILE* file)
{
    {
        std::lock_guard guard(lock);
        auto it = vbfFiles.find(file);
        if (it != vbfFiles.end())
        {
            if (size == 0 || count == 0)
            {
                return 0;
            }
            uint64_t total = static_cast<uint64_t>(size) * count;
            DWORD produced = 0;
            bool ok = total <= MAXDWORD
                && ReadVirtual(vbfReal, it->second.position, static_cast<uint8_t*>(buffer), static_cast<DWORD>(total), &produced);
            if (verbose || it->second.reads++ < 8 || it->second.position >= patch.appendedVirtualOffset)
            {
                Log("fread %llu at %llx -> %lu%s", total, it->second.position, produced, ok ? "" : " FAILED");
            }
            it->second.position += produced;
            return produced / size;
        }
    }
    return realFread(buffer, size, count, file);
}

// Returns true when the FILE is virtual and the seek was handled.
bool VirtualSeek(FILE* file, int64_t offset, int origin, int* result)
{
    std::lock_guard guard(lock);
    auto it = vbfFiles.find(file);
    if (it == vbfFiles.end())
    {
        return false;
    }
    int64_t base = origin == SEEK_CUR ? static_cast<int64_t>(it->second.position)
        : origin == SEEK_END ? static_cast<int64_t>(patch.appendedVirtualOffset + patch.appendedLength)
        : 0;
    if (base + offset < 0)
    {
        *result = -1;
        return true;
    }
    it->second.position = static_cast<uint64_t>(base + offset);
    *result = 0;
    return true;
}

bool VirtualTell(FILE* file, int64_t* position)
{
    std::lock_guard guard(lock);
    auto it = vbfFiles.find(file);
    if (it == vbfFiles.end())
    {
        return false;
    }
    *position = static_cast<int64_t>(it->second.position);
    return true;
}

int __cdecl HookFseek(FILE* file, long offset, int origin)
{
    int result;
    return VirtualSeek(file, offset, origin, &result) ? result : realFseek(file, offset, origin);
}

int __cdecl HookFseeki64(FILE* file, __int64 offset, int origin)
{
    int result;
    return VirtualSeek(file, offset, origin, &result) ? result : realFseeki64(file, offset, origin);
}

long __cdecl HookFtell(FILE* file)
{
    int64_t position;
    return VirtualTell(file, &position) ? static_cast<long>(position) : realFtell(file);
}

__int64 __cdecl HookFtelli64(FILE* file)
{
    int64_t position;
    return VirtualTell(file, &position) ? position : realFtelli64(file);
}

int __cdecl HookFgetpos(FILE* file, fpos_t* position)
{
    int64_t value;
    if (VirtualTell(file, &value))
    {
        *position = value;
        return 0;
    }
    return realFgetpos(file, position);
}

int __cdecl HookFsetpos(FILE* file, const fpos_t* position)
{
    int result;
    return VirtualSeek(file, *position, SEEK_SET, &result) ? result : realFsetpos(file, position);
}

int __cdecl HookFclose(FILE* file)
{
    {
        std::lock_guard guard(lock);
        vbfFiles.erase(file);
    }
    return realFclose(file);
}

// Replaces an import in the main executable's import table and returns the original target.
void* PatchImport(const char* module, const char* function, void* replacement)
{
    auto base = reinterpret_cast<uint8_t*>(GetModuleHandleW(nullptr));
    auto dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    auto nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
    auto& dir = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    for (auto desc = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(base + dir.VirtualAddress); desc->Name; desc++)
    {
        if (_stricmp(reinterpret_cast<char*>(base + desc->Name), module) != 0)
        {
            continue;
        }
        auto names = reinterpret_cast<IMAGE_THUNK_DATA*>(base + desc->OriginalFirstThunk);
        auto slots = reinterpret_cast<IMAGE_THUNK_DATA*>(base + desc->FirstThunk);
        for (; names->u1.AddressOfData; names++, slots++)
        {
            if (IMAGE_SNAP_BY_ORDINAL(names->u1.Ordinal))
            {
                continue;
            }
            auto byName = reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(base + names->u1.AddressOfData);
            if (strcmp(byName->Name, function) == 0)
            {
                DWORD old;
                VirtualProtect(&slots->u1.Function, sizeof(void*), PAGE_READWRITE, &old);
                void* original = reinterpret_cast<void*>(slots->u1.Function);
                slots->u1.Function = reinterpret_cast<ULONG_PTR>(replacement);
                VirtualProtect(&slots->u1.Function, sizeof(void*), old, &old);
                return original;
            }
        }
    }
    Log("import %s!%s not found", module, function);
    return nullptr;
}

template <typename T>
bool Hook(const char* module, const char* function, T replacement, T& original)
{
    void* previous = PatchImport(module, function, reinterpret_cast<void*>(replacement));
    if (previous)
    {
        original = reinterpret_cast<T>(previous);
    }
    return previous != nullptr;
}

void Initialize(HMODULE self)
{
    wchar_t path[MAX_PATH];
    GetModuleFileNameW(self, path, MAX_PATH);
    std::wstring dir(path);
    dir = dir.substr(0, dir.find_last_of(L'\\'));         // ...\x64
    gameDir = dir.substr(0, dir.find_last_of(L'\\'));     // game folder
    randoDir = gameDir + L"\\ff12rando";

    if (GetFileAttributesW(randoDir.c_str()) == INVALID_FILE_ATTRIBUTES)
    {
        return; // randomizer not set up: stay completely passive
    }

    // Shared so the log can be read while the game is running.
    logFile = _wfsopen((randoDir + L"\\loader.log").c_str(), L"w", _SH_DENYNO);
    verbose = GetFileAttributesW((randoDir + L"\\verbose.txt").c_str()) != INVALID_FILE_ATTRIBUTES;
    Log("ff12rando loader starting%s", verbose ? " (verbose)" : "");
    LoadPatch();
    if (!patchLoaded)
    {
        return;
    }

    vbfReal = CreateFileW((gameDir + L"\\FFXII_TZA.vbf").c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL, nullptr);
    if (vbfReal == INVALID_HANDLE_VALUE)
    {
        Log("could not open the archive, running unmodified");
        patchLoaded = false;
        return;
    }

    constexpr const char* kernel = "KERNEL32.dll";
    constexpr const char* stdio = "api-ms-win-crt-stdio-l1-1-0.dll";
    bool ok = Hook(kernel, "CreateFileA", &HookCreateFileA, realCreateFileA)
        & Hook(kernel, "ReadFile", &HookReadFile, realReadFile)
        & Hook(kernel, "SetFilePointer", &HookSetFilePointer, realSetFilePointer)
        & Hook(kernel, "CloseHandle", &HookCloseHandle, realCloseHandle)
        & Hook(stdio, "fopen", &HookFopen, realFopen)
        & Hook(stdio, "_wfopen", &HookWfopen, realWfopen)
        & Hook(stdio, "_wfopen_s", &HookWfopenS, realWfopenS)
        & Hook(stdio, "fread", &HookFread, realFread)
        & Hook(stdio, "fseek", &HookFseek, realFseek)
        & Hook(stdio, "_fseeki64", &HookFseeki64, realFseeki64)
        & Hook(stdio, "ftell", &HookFtell, realFtell)
        & Hook(stdio, "_ftelli64", &HookFtelli64, realFtelli64)
        & Hook(stdio, "fgetpos", &HookFgetpos, realFgetpos)
        & Hook(stdio, "fsetpos", &HookFsetpos, realFsetpos)
        & Hook(stdio, "fclose", &HookFclose, realFclose);
    Log(ok ? "hooks installed" : "some hooks could not be installed");
}
} // namespace

// ---- dinput8 forwarding -------------------------------------------------------------------------

extern "C" HRESULT WINAPI DirectInput8Create(HINSTANCE instance, DWORD version, REFIID riid, LPVOID* out, LPUNKNOWN outer)
{
    using Fn = HRESULT(WINAPI*)(HINSTANCE, DWORD, REFIID, LPVOID*, LPUNKNOWN);
    static Fn real = [] {
        wchar_t system[MAX_PATH];
        GetSystemDirectoryW(system, MAX_PATH);
        HMODULE module = LoadLibraryW((std::wstring(system) + L"\\dinput8.dll").c_str());
        return module ? reinterpret_cast<Fn>(GetProcAddress(module, "DirectInput8Create")) : nullptr;
    }();
    return real ? real(instance, version, riid, out, outer) : E_FAIL;
}

BOOL WINAPI DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(module);
        Initialize(module);
    }
    return TRUE;
}
