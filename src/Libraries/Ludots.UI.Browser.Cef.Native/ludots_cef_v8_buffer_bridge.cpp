#include <windows.h>

#include <algorithm>
#include <cstdint>
#include <cstring>
#include <cmath>
#include <mutex>
#include <string>
#include <vector>

#include "include/cef_v8.h"
#include "include/wrapper/cef_helpers.h"

namespace {

constexpr DWORD kRegistryBytes = 64 * 1024;
constexpr int kRegistryHeaderBytes = 32;
constexpr int kRegistryBufferRecordBytes = 544;
constexpr int kRegistryLiveRegionRecordBytes = 272;
constexpr int kRegistryBufferIdBytes = 256;
constexpr int kRegistryMemoryMapNameBytes = 256;
constexpr uint32_t kRegistryMagic = 0x3856444c; // LDV8
constexpr int32_t kRegistryVersion = 2;
constexpr wchar_t kRegistryEnvironmentVariable[] = L"LUDOTS_CEF_V8_BUFFER_REGISTRY";

struct SharedBufferInfo {
  std::string buffer_id;
  std::wstring memory_map_name;
  int32_t capacity_bytes = 0;
  int32_t header_bytes = 0;
};

struct SharedBufferLiveRegion {
  std::string buffer_id;
  int32_t byte_offset = 0;
  int32_t byte_length = 0;
  int64_t sequence = 0;
};

struct SharedBufferRegistrySnapshot {
  std::vector<SharedBufferInfo> buffers;
  std::vector<SharedBufferLiveRegion> live_regions;
};

struct Descriptor {
  std::string buffer_id;
  int32_t byte_offset = 0;
  int32_t byte_length = 0;
  double sequence = 0.0;
};

uint32_t ReadUInt32LE(const uint8_t* bytes) {
  return static_cast<uint32_t>(bytes[0]) |
         (static_cast<uint32_t>(bytes[1]) << 8) |
         (static_cast<uint32_t>(bytes[2]) << 16) |
         (static_cast<uint32_t>(bytes[3]) << 24);
}

int32_t ReadInt32LE(const uint8_t* bytes) {
  return static_cast<int32_t>(ReadUInt32LE(bytes));
}

int64_t ReadInt64LE(const uint8_t* bytes) {
  uint64_t value = static_cast<uint64_t>(bytes[0]) |
                   (static_cast<uint64_t>(bytes[1]) << 8) |
                   (static_cast<uint64_t>(bytes[2]) << 16) |
                   (static_cast<uint64_t>(bytes[3]) << 24) |
                   (static_cast<uint64_t>(bytes[4]) << 32) |
                   (static_cast<uint64_t>(bytes[5]) << 40) |
                   (static_cast<uint64_t>(bytes[6]) << 48) |
                   (static_cast<uint64_t>(bytes[7]) << 56);
  return static_cast<int64_t>(value);
}

std::string ReadUtf8Field(const uint8_t* bytes, size_t length) {
  size_t actual_length = 0;
  while (actual_length < length && bytes[actual_length] != 0) {
    actual_length++;
  }

  return std::string(reinterpret_cast<const char*>(bytes), actual_length);
}

std::wstring WidenAscii(const std::string& value) {
  std::wstring widened;
  widened.reserve(value.size());
  for (char c : value) {
    widened.push_back(static_cast<unsigned char>(c));
  }

  return widened;
}

std::wstring ReadRegistryNameFromEnvironment() {
  wchar_t value[512]{};
  DWORD length = GetEnvironmentVariableW(kRegistryEnvironmentVariable, value, static_cast<DWORD>(std::size(value)));
  if (length == 0 || length >= std::size(value)) {
    return std::wstring();
  }

  return std::wstring(value, length);
}

SharedBufferRegistrySnapshot ReadRegistry(std::string& error) {
  SharedBufferRegistrySnapshot snapshot;
  std::wstring registry_name = ReadRegistryNameFromEnvironment();
  if (registry_name.empty()) {
    error = "LUDOTS_CEF_V8_BUFFER_REGISTRY is not set in the render process.";
    return snapshot;
  }

  HANDLE mapping = OpenFileMappingW(FILE_MAP_READ, FALSE, registry_name.c_str());
  if (mapping == nullptr) {
    error = "Native V8 buffer registry memory map could not be opened.";
    return snapshot;
  }

  void* view = MapViewOfFile(mapping, FILE_MAP_READ, 0, 0, kRegistryBytes);
  if (view == nullptr) {
    CloseHandle(mapping);
    error = "Native V8 buffer registry view could not be mapped.";
    return snapshot;
  }

  const uint8_t* bytes = static_cast<const uint8_t*>(view);
  if (ReadUInt32LE(bytes) != kRegistryMagic) {
    UnmapViewOfFile(view);
    CloseHandle(mapping);
    error = "Native V8 buffer registry magic is invalid.";
    return snapshot;
  }

  if (ReadInt32LE(bytes + 4) != kRegistryVersion) {
    UnmapViewOfFile(view);
    CloseHandle(mapping);
    error = "Native V8 buffer registry version is unsupported.";
    return snapshot;
  }

  int32_t buffer_count = ReadInt32LE(bytes + 8);
  int32_t live_region_count = ReadInt32LE(bytes + 12);
  int32_t buffer_records_offset = ReadInt32LE(bytes + 16);
  int32_t live_region_records_offset = ReadInt32LE(bytes + 20);
  int32_t buffer_record_bytes = ReadInt32LE(bytes + 24);
  int32_t live_region_record_bytes = ReadInt32LE(bytes + 28);
  if (buffer_count < 0 ||
      live_region_count < 0 ||
      buffer_records_offset < kRegistryHeaderBytes ||
      live_region_records_offset < buffer_records_offset ||
      buffer_record_bytes != kRegistryBufferRecordBytes ||
      live_region_record_bytes != kRegistryLiveRegionRecordBytes ||
      static_cast<int64_t>(buffer_records_offset) +
          (static_cast<int64_t>(buffer_count) * buffer_record_bytes) >
          kRegistryBytes ||
      static_cast<int64_t>(live_region_records_offset) +
          (static_cast<int64_t>(live_region_count) * live_region_record_bytes) >
          kRegistryBytes) {
    UnmapViewOfFile(view);
    CloseHandle(mapping);
    error = "Native V8 buffer registry layout is invalid.";
    return snapshot;
  }

  snapshot.buffers.reserve(static_cast<size_t>(buffer_count));
  for (int32_t i = 0; i < buffer_count; i++) {
    const uint8_t* record = bytes + buffer_records_offset + (static_cast<size_t>(i) * buffer_record_bytes);
    SharedBufferInfo buffer;
    buffer.buffer_id = ReadUtf8Field(record, kRegistryBufferIdBytes);
    std::string memory_map_name = ReadUtf8Field(record + kRegistryBufferIdBytes, kRegistryMemoryMapNameBytes);
    buffer.memory_map_name = WidenAscii(memory_map_name);
    buffer.capacity_bytes = ReadInt32LE(record + kRegistryBufferIdBytes + kRegistryMemoryMapNameBytes);
    buffer.header_bytes = ReadInt32LE(record + kRegistryBufferIdBytes + kRegistryMemoryMapNameBytes + sizeof(int32_t));
    if (!buffer.buffer_id.empty() && !buffer.memory_map_name.empty()) {
      snapshot.buffers.push_back(std::move(buffer));
    }
  }

  snapshot.live_regions.reserve(static_cast<size_t>(live_region_count));
  for (int32_t i = 0; i < live_region_count; i++) {
    const uint8_t* record = bytes + live_region_records_offset + (static_cast<size_t>(i) * live_region_record_bytes);
    SharedBufferLiveRegion live_region;
    live_region.buffer_id = ReadUtf8Field(record, kRegistryBufferIdBytes);
    live_region.byte_offset = ReadInt32LE(record + kRegistryBufferIdBytes);
    live_region.byte_length = ReadInt32LE(record + kRegistryBufferIdBytes + sizeof(int32_t));
    live_region.sequence = ReadInt64LE(record + kRegistryBufferIdBytes + (sizeof(int32_t) * 2));
    if (!live_region.buffer_id.empty()) {
      snapshot.live_regions.push_back(std::move(live_region));
    }
  }

  UnmapViewOfFile(view);
  CloseHandle(mapping);
  return snapshot;
}

bool TryFindBuffer(const std::string& buffer_id,
                   SharedBufferRegistrySnapshot& snapshot,
                   SharedBufferInfo& buffer,
                   std::string& error) {
  snapshot = ReadRegistry(error);
  if (!error.empty()) {
    return false;
  }

  auto found = std::find_if(
      snapshot.buffers.begin(),
      snapshot.buffers.end(),
      [&buffer_id](const SharedBufferInfo& candidate) {
        return candidate.buffer_id == buffer_id;
      });
  if (found == snapshot.buffers.end()) {
    error = "Shared buffer is not registered for native V8 access: " + buffer_id;
    return false;
  }

  buffer = *found;
  return true;
}

bool TryGetProperty(CefRefPtr<CefV8Value> object,
                    const char* name,
                    CefRefPtr<CefV8Value>& value) {
  if (!object || !object->IsObject()) {
    return false;
  }

  value = object->GetValue(name);
  return value.get() != nullptr;
}

bool TryReadString(CefRefPtr<CefV8Value> object,
                   const char* camel_name,
                   const char* pascal_name,
                   std::string& value) {
  CefRefPtr<CefV8Value> property;
  if (!TryGetProperty(object, camel_name, property)) {
    TryGetProperty(object, pascal_name, property);
  }

  if (!property || !property->IsString()) {
    return false;
  }

  value = property->GetStringValue().ToString();
  return true;
}

bool TryReadInt(CefRefPtr<CefV8Value> object,
                const char* camel_name,
                const char* pascal_name,
                int32_t& value) {
  CefRefPtr<CefV8Value> property;
  if (!TryGetProperty(object, camel_name, property)) {
    TryGetProperty(object, pascal_name, property);
  }

  if (!property) {
    return false;
  }

  if (property->IsInt()) {
    value = property->GetIntValue();
    return true;
  }

  if (property->IsUInt()) {
    uint32_t unsigned_value = property->GetUIntValue();
    if (unsigned_value > static_cast<uint32_t>(INT32_MAX)) {
      return false;
    }

    value = static_cast<int32_t>(unsigned_value);
    return true;
  }

  if (property->IsDouble()) {
    double double_value = property->GetDoubleValue();
    if (double_value < static_cast<double>(INT32_MIN) ||
        double_value > static_cast<double>(INT32_MAX)) {
      return false;
    }

    value = static_cast<int32_t>(double_value);
    return true;
  }

  return false;
}

bool TryReadDouble(CefRefPtr<CefV8Value> object,
                   const char* camel_name,
                   const char* pascal_name,
                   double& value) {
  CefRefPtr<CefV8Value> property;
  if (!TryGetProperty(object, camel_name, property)) {
    TryGetProperty(object, pascal_name, property);
  }

  if (!property) {
    return false;
  }

  if (property->IsInt()) {
    value = static_cast<double>(property->GetIntValue());
    return true;
  }

  if (property->IsUInt()) {
    value = static_cast<double>(property->GetUIntValue());
    return true;
  }

  if (property->IsDouble()) {
    value = property->GetDoubleValue();
    return true;
  }

  return false;
}

bool TryParseDescriptor(CefRefPtr<CefV8Value> value, Descriptor& descriptor, std::string& error) {
  if (!value || !value->IsObject()) {
    error = "acquireV8Buffer expects a descriptor object.";
    return false;
  }

  if (!TryReadString(value, "bufferId", "BufferId", descriptor.buffer_id) ||
      descriptor.buffer_id.empty()) {
    error = "Shared-buffer descriptor is missing bufferId.";
    return false;
  }

  if (!TryReadInt(value, "byteOffset", "ByteOffset", descriptor.byte_offset)) {
    error = "Shared-buffer descriptor is missing byteOffset.";
    return false;
  }

  if (!TryReadInt(value, "byteLength", "ByteLength", descriptor.byte_length)) {
    error = "Shared-buffer descriptor is missing byteLength.";
    return false;
  }

  if (!TryReadDouble(value, "sequence", "Sequence", descriptor.sequence)) {
    error = "Shared-buffer descriptor is missing sequence.";
    return false;
  }

  return true;
}

bool ValidateDescriptorRange(const Descriptor& descriptor,
                             const SharedBufferInfo& buffer,
                             const std::vector<SharedBufferLiveRegion>& live_regions,
                             std::string& error) {
  if (descriptor.byte_offset < buffer.header_bytes) {
    error = "Shared-buffer descriptor byteOffset is outside the payload region.";
    return false;
  }

  if (descriptor.byte_length < 0) {
    error = "Shared-buffer descriptor byteLength must be non-negative.";
    return false;
  }

  int64_t end = static_cast<int64_t>(descriptor.byte_offset) + descriptor.byte_length;
  if (end > buffer.capacity_bytes) {
    error = "Shared-buffer descriptor byte range exceeds the native buffer capacity.";
    return false;
  }

  if (descriptor.sequence <= 0) {
    error = "Shared-buffer descriptor sequence must be positive.";
    return false;
  }

  if (std::floor(descriptor.sequence) != descriptor.sequence) {
    error = "Shared-buffer descriptor sequence must be an integer.";
    return false;
  }

  int64_t sequence = static_cast<int64_t>(descriptor.sequence);
  auto found = std::find_if(
      live_regions.begin(),
      live_regions.end(),
      [&descriptor, sequence](const SharedBufferLiveRegion& live_region) {
        return live_region.buffer_id == descriptor.buffer_id &&
               live_region.byte_offset == descriptor.byte_offset &&
               live_region.byte_length == descriptor.byte_length &&
               live_region.sequence == sequence;
      });
  if (found == live_regions.end()) {
    error = "Shared-buffer descriptor range is no longer active for native V8 access.";
    return false;
  }

  return true;
}

class AcquireV8BufferHandler final : public CefV8Handler {
 public:
  AcquireV8BufferHandler() = default;

  bool Execute(const CefString& name,
               CefRefPtr<CefV8Value> object,
               const CefV8ValueList& arguments,
               CefRefPtr<CefV8Value>& retval,
               CefString& exception) override {
    (void)object;
    if (name.ToString() != "acquireV8Buffer") {
      return false;
    }

    if (arguments.size() != 1) {
      exception = "acquireV8Buffer expects exactly one descriptor.";
      return true;
    }

    Descriptor descriptor;
    std::string error;
    if (!TryParseDescriptor(arguments[0], descriptor, error)) {
      exception = error;
      return true;
    }

    SharedBufferRegistrySnapshot snapshot;
    SharedBufferInfo buffer;
    if (!TryFindBuffer(descriptor.buffer_id, snapshot, buffer, error)) {
      exception = error;
      return true;
    }

    if (!ValidateDescriptorRange(descriptor, buffer, snapshot.live_regions, error)) {
      exception = error;
      return true;
    }

    HANDLE mapping = OpenFileMappingW(FILE_MAP_READ, FALSE, buffer.memory_map_name.c_str());
    if (mapping == nullptr) {
      exception = "Shared-buffer memory map could not be opened for native V8 access.";
      return true;
    }

    void* view = MapViewOfFile(mapping, FILE_MAP_READ, 0, 0, static_cast<SIZE_T>(buffer.capacity_bytes));
    if (view == nullptr) {
      CloseHandle(mapping);
      exception = "Shared-buffer memory map view could not be mapped for native V8 access.";
      return true;
    }

    CefRefPtr<CefV8BackingStore> backing_store =
        CefV8BackingStore::Create(static_cast<size_t>(descriptor.byte_length));
    if (!backing_store || !backing_store->IsValid() || backing_store->Data() == nullptr) {
      UnmapViewOfFile(view);
      CloseHandle(mapping);
      exception = "CEF V8 backing store could not be created for native ArrayBuffer access.";
      return true;
    }

    const void* data = static_cast<const uint8_t*>(view) + descriptor.byte_offset;
    std::memcpy(backing_store->Data(), data, static_cast<size_t>(descriptor.byte_length));
    UnmapViewOfFile(view);
    CloseHandle(mapping);

    retval = CefV8Value::CreateArrayBufferFromBackingStore(backing_store);
    if (!retval) {
      exception = "CEF CreateArrayBufferFromBackingStore returned null.";
      return true;
    }

    return true;
  }

 private:
  IMPLEMENT_REFCOUNTING(AcquireV8BufferHandler);
  DISALLOW_COPY_AND_ASSIGN(AcquireV8BufferHandler);
};

bool g_installed = false;
std::mutex g_install_mutex;

}  // namespace

extern "C" __declspec(dllexport) int LudotsCefV8Install() {
  std::lock_guard<std::mutex> lock(g_install_mutex);
  if (g_installed) {
    return 0;
  }

  const char extension_code[] =
      "var __ludotsCefV8;"
      "if (!__ludotsCefV8) __ludotsCefV8 = {};"
      "native function acquireV8Buffer();"
      "__ludotsCefV8.acquireV8Buffer = function(descriptor) {"
      "  return acquireV8Buffer(descriptor);"
      "};";
  bool registered = CefRegisterExtension(
      "v8/ludots_dataplane_buffer_bridge",
      extension_code,
      new AcquireV8BufferHandler());
  if (!registered) {
    return 1;
  }

  g_installed = true;
  return 0;
}
