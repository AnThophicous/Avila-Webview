using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Avila.Bridge;

public sealed partial class BridgeCommandRegistry
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> StoreLocks = new(StringComparer.OrdinalIgnoreCase);

    private void RegisterStorageCommands()
    {
        Register("store.get", async (context, request, token) =>
        {
            var address = ReadStoreAddress(context, request.Payload, requireKey: true);
            return await WithStoreAsync(context, address, async store =>
            {
                await Task.CompletedTask;
                var exists = store.TryGetPropertyValue(address.Key, out var value);
                return new { exists, value };
            }, token).ConfigureAwait(false);
        });

        Register("store.set", async (context, request, token) =>
        {
            var address = ReadStoreAddress(context, request.Payload, requireKey: true);
            if (!request.Payload.TryGetProperty("value", out var value))
            {
                throw new BridgeException(BridgeErrorCodes.InvalidRequest, "payload.value is required.");
            }

            return await WithStoreAsync(context, address, async store =>
            {
                store[address.Key] = JsonNode.Parse(value.GetRawText());
                await WriteStoreAsync(address.Path, store, context.Project.Manifest.Storage.MaxStoreBytes, token).ConfigureAwait(false);
                return new { written = true };
            }, token).ConfigureAwait(false);
        });

        Register("store.delete", async (context, request, token) =>
        {
            var address = ReadStoreAddress(context, request.Payload, requireKey: true);
            return await WithStoreAsync(context, address, async store =>
            {
                var deleted = store.Remove(address.Key);
                await WriteStoreAsync(address.Path, store, context.Project.Manifest.Storage.MaxStoreBytes, token).ConfigureAwait(false);
                return new { deleted };
            }, token).ConfigureAwait(false);
        });

        Register("store.clear", async (context, request, token) =>
        {
            var address = ReadStoreAddress(context, request.Payload, requireKey: false);
            return await WithStoreAsync(context, address, async store =>
            {
                store.Clear();
                await WriteStoreAsync(address.Path, store, context.Project.Manifest.Storage.MaxStoreBytes, token).ConfigureAwait(false);
                return new { cleared = true };
            }, token).ConfigureAwait(false);
        });

        Register("store.export", async (context, request, token) =>
        {
            var address = ReadStoreAddress(context, request.Payload, requireKey: false);
            return await WithStoreAsync(context, address, async store =>
            {
                await Task.CompletedTask;
                return new { data = store.DeepClone() };
            }, token).ConfigureAwait(false);
        });

        Register("store.import", async (context, request, token) =>
        {
            var address = ReadStoreAddress(context, request.Payload, requireKey: false);
            if (!request.Payload.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                throw new BridgeException(BridgeErrorCodes.InvalidRequest, "payload.data must be an object.");
            }

            var merge = PayloadReader.GetOptionalBoolean(request.Payload, "merge", true);
            return await WithStoreAsync(context, address, async store =>
            {
                var incoming = JsonNode.Parse(data.GetRawText())?.AsObject()
                    ?? throw new BridgeException(BridgeErrorCodes.InvalidRequest, "payload.data must be an object.");
                if (!merge)
                {
                    store.Clear();
                }

                foreach (var item in incoming)
                {
                    store[item.Key] = item.Value?.DeepClone();
                }

                await WriteStoreAsync(address.Path, store, context.Project.Manifest.Storage.MaxStoreBytes, token).ConfigureAwait(false);
                return new { imported = true };
            }, token).ConfigureAwait(false);
        });

        Register("secrets.get", async (context, request, token) =>
        {
            EnsureSecretsEnabled(context);
            var address = ReadSecretAddress(context, request.Payload, requireKey: true);
            return await WithStoreAsync(context, address, async store =>
            {
                await Task.CompletedTask;
                if (!store.TryGetPropertyValue(address.Key, out var value) || value is null)
                {
                    return new { exists = false, value = (string?)null };
                }

                var protectedValue = value.GetValue<string>();
                return new { exists = true, value = context.Runtime.UnprotectSecret(protectedValue) };
            }, token).ConfigureAwait(false);
        });

        Register("secrets.set", async (context, request, token) =>
        {
            EnsureSecretsEnabled(context);
            var address = ReadSecretAddress(context, request.Payload, requireKey: true);
            var value = PayloadReader.GetString(request.Payload, "value", context.Project.Manifest.Security.MaxPayloadBytes);
            return await WithStoreAsync(context, address, async store =>
            {
                store[address.Key] = context.Runtime.ProtectSecret(value);
                await WriteStoreAsync(address.Path, store, context.Project.Manifest.Storage.MaxStoreBytes, token).ConfigureAwait(false);
                return new { written = true };
            }, token).ConfigureAwait(false);
        });

        Register("secrets.delete", async (context, request, token) =>
        {
            EnsureSecretsEnabled(context);
            var address = ReadSecretAddress(context, request.Payload, requireKey: true);
            return await WithStoreAsync(context, address, async store =>
            {
                var deleted = store.Remove(address.Key);
                await WriteStoreAsync(address.Path, store, context.Project.Manifest.Storage.MaxStoreBytes, token).ConfigureAwait(false);
                return new { deleted };
            }, token).ConfigureAwait(false);
        });

        Register("secrets.clear", async (context, request, token) =>
        {
            EnsureSecretsEnabled(context);
            var address = ReadSecretAddress(context, request.Payload, requireKey: false);
            return await WithStoreAsync(context, address, async store =>
            {
                store.Clear();
                await WriteStoreAsync(address.Path, store, context.Project.Manifest.Storage.MaxStoreBytes, token).ConfigureAwait(false);
                return new { cleared = true };
            }, token).ConfigureAwait(false);
        });
    }

    private static async Task<object?> WithStoreAsync(
        BridgeCommandContext context,
        StoreAddress address,
        Func<JsonObject, Task<object?>> action,
        CancellationToken cancellationToken)
    {
        var gate = StoreLocks.GetOrAdd(address.Path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var store = await ReadStoreAsync(address.Path, cancellationToken).ConfigureAwait(false);
            return await context.Workers.EnqueueAsync(_ => action(store), cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<JsonObject> ReadStoreAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var text = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        try
        {
            return JsonNode.Parse(text)?.AsObject() ?? [];
        }
        catch (JsonException)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, "Store file is not valid JSON.");
        }
    }

    private static async Task WriteStoreAsync(string path, JsonObject store, long maxBytes, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = store.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        if (Encoding.UTF8.GetByteCount(json) > maxBytes)
        {
            throw new BridgeException(BridgeErrorCodes.PayloadTooLarge, "Store is larger than storage.maxStoreBytes.");
        }

        var tempPath = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static StoreAddress ReadStoreAddress(BridgeCommandContext context, JsonElement payload, bool requireKey)
    {
        var store = PayloadReader.GetString(payload, "store", 32, required: false);
        if (string.IsNullOrWhiteSpace(store))
        {
            store = "data";
        }

        if (!context.Project.Manifest.Storage.AllowedStores.Contains(store, StringComparer.OrdinalIgnoreCase))
        {
            throw new BridgeException(BridgeErrorCodes.PermissionDenied, $"Store is not allowed by storage.allowedStores: {store}");
        }

        var ns = PayloadReader.GetString(payload, "namespace", 64, required: false);
        if (string.IsNullOrWhiteSpace(ns))
        {
            ns = "default";
        }

        ValidateStoreName(ns, "namespace");
        var key = PayloadReader.GetString(payload, "key", 128, required: requireKey);
        if (requireKey)
        {
            ValidateStoreName(key, "key");
        }

        var root = context.Runtime.GetPath(store);
        var path = Path.Combine(root, "store", $"{ns}.json");
        return new StoreAddress(store, ns, key, path);
    }

    private static StoreAddress ReadSecretAddress(BridgeCommandContext context, JsonElement payload, bool requireKey)
    {
        var ns = PayloadReader.GetString(payload, "namespace", 64, required: false);
        if (string.IsNullOrWhiteSpace(ns))
        {
            ns = "default";
        }

        ValidateStoreName(ns, "namespace");
        var key = PayloadReader.GetString(payload, "key", 128, required: requireKey);
        if (requireKey)
        {
            ValidateStoreName(key, "key");
        }

        var path = Path.Combine(context.Runtime.GetPath("data"), "secrets", $"{ns}.json");
        return new StoreAddress("data", ns, key, path);
    }

    private static void ValidateStoreName(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"payload.{name} is invalid.");
        }

        foreach (var character in value)
        {
            if (character is '.' or '-' or '_' || char.IsAsciiLetterOrDigit(character))
            {
                continue;
            }

            throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"payload.{name} contains an unsafe character.");
        }
    }

    private static void EnsureSecretsEnabled(BridgeCommandContext context)
    {
        if (!context.Project.Manifest.Storage.Secrets && !context.Mode.Equals("dev", StringComparison.OrdinalIgnoreCase))
        {
            throw new BridgeException(BridgeErrorCodes.PermissionDenied, "Encrypted secrets are disabled by storage.secrets.");
        }
    }

    private sealed record StoreAddress(string Store, string Namespace, string Key, string Path);
}
