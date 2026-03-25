using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Blueprint.App.Backend;
using Blueprint.App.Models;

namespace Blueprint.App;

/// <summary>
/// Manages Blueprint license state (Free / Pro) via Lemon Squeezy License API.
///
/// Activation  :  online only — calls LS /v1/licenses/activate.
/// Validation  :  cached on disk; revalidated in background on startup (Opcija A).
///                If offline at startup, cached Pro status is honoured.
/// Deactivation:  calls LS /v1/licenses/deactivate, then clears local cache.
/// Storage     :  parametri table, Idpoglavlja = -1:
///                  "LicenseKey"        — the raw license key
///                  "LicenseInstanceId" — the LS instance UUID for this machine
///
/// Free tier   :  SQLite + Access: full CRUD, Schema Import, Schema Sync.
///                Transfer Data Wizard: up to FreeTransferLimit runs, then Pro required.
/// Pro tier    :  All backends + unlimited Transfer Data Wizard.
/// </summary>
public static class LicenseService
{
    // ── Constants ────────────────────────────────────────────────────────────

    public const int FreeTransferLimit = 5;

    private const int    LicenseChapterId    = -1;
    private const string LicenseKeyParam     = "LicenseKey";
    private const string InstanceIdParam     = "LicenseInstanceId";
    private const string TransferUsesParam   = "TransferWizardUses";

    private const string LsActivateUrl   = "https://api.lemonsqueezy.com/v1/licenses/activate";
    private const string LsValidateUrl   = "https://api.lemonsqueezy.com/v1/licenses/validate";
    private const string LsDeactivateUrl = "https://api.lemonsqueezy.com/v1/licenses/deactivate";

    // ── Test / Production switch ──────────────────────────────────────────────
    // Set IsTestMode = false and update LsProductId when going live.

#if DEBUG
    public const bool IsTestMode    = true;
#else
    public const bool IsTestMode    = false;
#endif

    private const int LsTestProductId = 907641;   // BlueprintDB Pro — LS Test Mode
    private const int LsLiveProductId = 907641;   // TODO: replace with live product ID after LS verification

    private static int LsProductId => IsTestMode ? LsTestProductId : LsLiveProductId;

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    // ── State ────────────────────────────────────────────────────────────────

    public static LicenseTier CurrentTier { get; private set; } = LicenseTier.Free;
    public static string?     ActiveKey   { get; private set; }
    private static string?    _instanceId;

    public static bool IsPro => CurrentTier == LicenseTier.Pro;

    // ── Feature gates ────────────────────────────────────────────────────────

    public static bool CanUseBackend(BackendType type)
        => type is BackendType.SQLite or BackendType.Access || IsPro;

    public static bool CanUseSchemaImport(BackendType type)
        => type is BackendType.SQLite or BackendType.Access || IsPro;

    public static bool CanUseSchemaSyncWith(BackendType type)
        => type is BackendType.SQLite or BackendType.Access || IsPro;

    public static bool CanUseTransferWizard
        => IsPro || GetTransferWizardUses() < FreeTransferLimit;

    public static int FreeTransferRunsRemaining
        => IsPro ? int.MaxValue : Math.Max(0, FreeTransferLimit - GetTransferWizardUses());

    // ── Transfer usage counter ────────────────────────────────────────────────

    public static int GetTransferWizardUses()
    {
        try
        {
            using var db = new BlueprintDbContext();
            var param = db.Parametris.FirstOrDefault(p =>
                p.Idpoglavlja == LicenseChapterId &&
                p.Nazivparametra == TransferUsesParam);

            if (param?.Ocitano != null && int.TryParse(param.Ocitano, out var count))
                return count;
        }
        catch (Exception ex) { LogService.Error("License", "Failed to read transfer uses", ex); }
        return 0;
    }

    public static void IncrementTransferWizardUses()
    {
        if (IsPro) return;

        try
        {
            using var db = new BlueprintDbContext();
            var param = db.Parametris.FirstOrDefault(p =>
                p.Idpoglavlja == LicenseChapterId &&
                p.Nazivparametra == TransferUsesParam);

            var current = 0;
            if (param?.Ocitano != null) int.TryParse(param.Ocitano, out current);

            if (param == null)
                db.Parametris.Add(new Parametri
                {
                    Idpoglavlja    = LicenseChapterId,
                    Nazivparametra = TransferUsesParam,
                    Ocitano        = (current + 1).ToString()
                });
            else
                param.Ocitano = (current + 1).ToString();

            db.SaveChanges();
        }
        catch (Exception ex) { LogService.Error("License", "Failed to increment transfer uses", ex); }
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads cached license from DB (instant), then revalidates in background.
    /// Call once at startup from App.xaml.cs.
    /// </summary>
    public static void Initialize()
    {
#if DEBUG
        // Developer override — full Pro in Debug builds, no license check needed
        CurrentTier = LicenseTier.Pro;
        ActiveKey   = "DEV-BUILD";
        return;
#endif
        try
        {
            using var db = new BlueprintDbContext();

            var keyParam = db.Parametris.FirstOrDefault(p =>
                p.Idpoglavlja == LicenseChapterId &&
                p.Nazivparametra == LicenseKeyParam);

            var instParam = db.Parametris.FirstOrDefault(p =>
                p.Idpoglavlja == LicenseChapterId &&
                p.Nazivparametra == InstanceIdParam);

            if (keyParam?.Ocitano != null)
            {
                ActiveKey   = keyParam.Ocitano;
                _instanceId = instParam?.Ocitano;
                CurrentTier = LicenseTier.Pro;  // Trust cache; LS revalidates in background
            }
        }
        catch (Exception ex) { LogService.Error("License", "Failed to load license cache", ex); }

        // Background revalidation — does not block startup
        if (ActiveKey != null)
            _ = RevalidateInBackgroundAsync();
    }

    private static async Task RevalidateInBackgroundAsync()
    {
        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["license_key"] = ActiveKey!,
                ["instance_id"] = _instanceId ?? ""
            });

            var response = await _http.PostAsync(LsValidateUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                // Key revoked or deactivated on the server — downgrade silently
                LogService.Info("License", "Cached license key rejected by server — reverting to Free.");
                CurrentTier = LicenseTier.Free;
                ActiveKey   = null;
                _instanceId = null;
                DeletePersistedKey();
                return;
            }

            // Verify product ID on revalidation as well
            var json = await response.Content.ReadAsStringAsync();
            var doc  = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("meta", out var meta) &&
                meta.TryGetProperty("product_id", out var productIdEl) &&
                productIdEl.GetInt32() != LsProductId)
            {
                LogService.Info("License", "Revalidation rejected — wrong product ID.");
                CurrentTier = LicenseTier.Free;
                ActiveKey   = null;
                _instanceId = null;
                DeletePersistedKey();
            }
        }
        catch
        {
            // No internet — keep cached Pro status (Opcija A: offline-friendly)
        }
    }

    // ── Activation ────────────────────────────────────────────────────────────

    public static async Task<LicenseActivationResult> ActivateAsync(string key)
    {
        var normalized = key.Trim().ToUpperInvariant();

        if (IsPro && normalized == ActiveKey)
            return LicenseActivationResult.AlreadyActive;

        try
        {
            var machineName = $"{Environment.MachineName} ({Environment.UserName})";
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["license_key"]   = normalized,
                ["instance_name"] = machineName
            });

            var response = await _http.PostAsync(LsActivateUrl, content);
            var json     = await response.Content.ReadAsStringAsync();
            var doc      = JsonDocument.Parse(json);

            if (response.IsSuccessStatusCode &&
                doc.RootElement.TryGetProperty("activated", out var activated) &&
                activated.GetBoolean())
            {
                // Verify the key belongs to our product
                if (doc.RootElement.TryGetProperty("meta", out var meta) &&
                    meta.TryGetProperty("product_id", out var productIdEl) &&
                    productIdEl.GetInt32() != LsProductId)
                {
                    LogService.Info("License", $"Activation rejected — wrong product ID: {productIdEl.GetInt32()}");
                    return LicenseActivationResult.InvalidKey;
                }

                var instanceId = doc.RootElement
                    .GetProperty("instance")
                    .GetProperty("id")
                    .GetString();

                CurrentTier = LicenseTier.Pro;
                ActiveKey   = normalized;
                _instanceId = instanceId;
                PersistKey(normalized, instanceId);
                LogService.Info("License", $"License activated. Instance: {instanceId}");
                return LicenseActivationResult.Success;
            }

            // Inspect error message from LS
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                var msg = error.GetString() ?? "";
                if (msg.Contains("limit", StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains("exceeded", StringComparison.OrdinalIgnoreCase))
                    return LicenseActivationResult.KeyExhausted;
            }

            return LicenseActivationResult.InvalidKey;
        }
        catch (Exception ex)
        {
            LogService.Error("License", "Network error during activation", ex);
            return LicenseActivationResult.NetworkError;
        }
    }

    // ── Deactivation ──────────────────────────────────────────────────────────

    public static async Task DeactivateAsync()
    {
        if (ActiveKey != null && _instanceId != null)
        {
            try
            {
                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["license_key"] = ActiveKey,
                    ["instance_id"] = _instanceId
                });
                await _http.PostAsync(LsDeactivateUrl, content);
                LogService.Info("License", "License deactivated on server.");
            }
            catch { /* Deactivation is best-effort; local cache is cleared regardless */ }
        }

        CurrentTier = LicenseTier.Free;
        ActiveKey   = null;
        _instanceId = null;
        DeletePersistedKey();
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private static void PersistKey(string key, string? instanceId)
    {
        try
        {
            using var db = new BlueprintDbContext();

            UpsertParam(db, LicenseKeyParam, key);
            if (instanceId != null)
                UpsertParam(db, InstanceIdParam, instanceId);

            db.SaveChanges();
        }
        catch (Exception ex) { LogService.Error("License", "Failed to save license key", ex); }
    }

    private static void UpsertParam(BlueprintDbContext db, string name, string value)
    {
        var p = db.Parametris.FirstOrDefault(x =>
            x.Idpoglavlja == LicenseChapterId && x.Nazivparametra == name);
        if (p == null)
            db.Parametris.Add(new Parametri
                { Idpoglavlja = LicenseChapterId, Nazivparametra = name, Ocitano = value });
        else
            p.Ocitano = value;
    }

    private static void DeletePersistedKey()
    {
        try
        {
            using var db = new BlueprintDbContext();
            var toRemove = db.Parametris
                .Where(p => p.Idpoglavlja == LicenseChapterId &&
                           (p.Nazivparametra == LicenseKeyParam ||
                            p.Nazivparametra == InstanceIdParam))
                .ToList();
            db.Parametris.RemoveRange(toRemove);
            db.SaveChanges();
        }
        catch (Exception ex) { LogService.Error("License", "Failed to clear license cache", ex); }
    }
}
