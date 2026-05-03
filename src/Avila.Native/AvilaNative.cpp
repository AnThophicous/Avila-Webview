#include <cstdint>
#include <cstdio>
#include <cstring>

#if defined(_WIN32)
#define AVILA_EXPORT extern "C" __declspec(dllexport)
#else
#define AVILA_EXPORT extern "C"
#endif

namespace {
constexpr uint64_t FnvOffset = 14695981039346656037ull;
constexpr uint64_t FnvPrime = 1099511628211ull;

uint64_t fnv1a64(const uint8_t* data, int length) {
    uint64_t hash = FnvOffset;
    for (int index = 0; index < length; ++index) {
        hash ^= static_cast<uint64_t>(data[index]);
        hash *= FnvPrime;
    }

    return hash;
}
}

AVILA_EXPORT int avila_fast_hash(const uint8_t* data, int length, char* output, int outputLength) {
    if (data == nullptr || output == nullptr || length < 0 || outputLength < 17) {
        return -1;
    }

    const uint64_t hash = fnv1a64(data, length);
#if defined(_WIN32)
    const int written = sprintf_s(output, static_cast<size_t>(outputLength), "%016llx", hash);
#else
    const int written = std::snprintf(output, static_cast<size_t>(outputLength), "%016llx", static_cast<unsigned long long>(hash));
#endif
    if (written <= 0 || written >= outputLength) {
        if (outputLength > 0) {
            output[0] = '\0';
        }
        return -2;
    }

    return 0;
}
