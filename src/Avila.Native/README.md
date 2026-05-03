# Avila.Native

Optional C++ core for heavy native work.

- C ABI exports keep P/Invoke stable.
- Caller owns input buffers.
- The native DLL never returns heap pointers.
- Every output buffer is caller-allocated and length-checked.
- Internal code uses RAII and avoids global mutable state.

Build example:

```powershell
cmake -S src/Avila.Native -B build/native
cmake --build build/native --config Release
```
