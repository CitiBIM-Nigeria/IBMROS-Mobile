using System;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.CognitoIdentity;
using Amazon.CognitoIdentityProvider;
using Amazon.S3;
using Amazon.DynamoDBv2;
using Amazon.Runtime;
using UnityEngine;

public class AwsManager : MonoBehaviour
{
    public static AwsManager Instance { get; private set; }

    public AmazonS3Client S3Client { get; private set; }
    public AmazonDynamoDBClient DynamoDBClient { get; private set; }
    public AmazonCognitoIdentityProviderClient CognitoProvider { get; private set; }

    private CognitoAWSCredentials _credentials;
    private bool _isInitialized = false;
    public bool IsInitialized => _isInitialized;

    // Other scripts subscribe to this event to know when AWS is ready
    public static event Action OnAwsReady;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    async void Start()
    {
        await InitializeGuestWithRetry();
    }

    // Retries initialization with exponential backoff
    // Handles cases where the device has no network on app start
    private async Task InitializeGuestWithRetry()
    {
        int maxRetries = 5;
        int delayMs = 1000;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            bool success = TryInitializeGuest();

            if (success)
            {
                _isInitialized = true;
                Debug.Log("[AwsManager] Guest initialization successful.");

                // Fire the event so all waiting scripts proceed
                OnAwsReady?.Invoke();
                return;
            }

            Debug.LogWarning($"[AwsManager] Initialization attempt {attempt} failed. Retrying in {delayMs}ms...");
            await Task.Delay(delayMs);

            // Double the delay each retry: 1s, 2s, 4s, 8s, 16s
            delayMs *= 2;
        }

        // All retries failed
        Debug.LogError("[AwsManager] AWS initialization failed after all retries. Check network connection.");
        
        // Fire event even on failure so UI can show an error instead of freezing
        OnAwsReady?.Invoke();
    }

    private bool TryInitializeGuest()
    {
        try
        {
            var region = RegionEndpoint.GetBySystemName(AwsConfig.Region);

            _credentials = new CognitoAWSCredentials(
                AwsConfig.IdentityPoolId,
                region
            );

            CognitoProvider = new AmazonCognitoIdentityProviderClient(
                new AnonymousAWSCredentials(),
                region
            );

            S3Client = new AmazonS3Client(_credentials, region);
            DynamoDBClient = BuildDynamoDBClient(region);

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[AwsManager] Init error: {e.Message}");
            return false;
        }
    }

    public void UpgradeToAuthenticated(string idToken)
    {
        try
        {
            var region = RegionEndpoint.GetBySystemName(AwsConfig.Region);
            _credentials.AddLogin(AwsConfig.CognitoProviderName, idToken);
            S3Client = new AmazonS3Client(_credentials, region);
            DynamoDBClient = BuildDynamoDBClient(region);
            Debug.Log("[AwsManager] Upgraded to authenticated credentials.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[AwsManager] Credential upgrade failed: {e.Message}");
        }
    }

    public void DowngradeToGuest()
    {
        try
        {
            _credentials.RemoveLogin(AwsConfig.CognitoProviderName);
            _credentials.ClearCredentials();
            var region = RegionEndpoint.GetBySystemName(AwsConfig.Region);
            S3Client = new AmazonS3Client(_credentials, region);
            DynamoDBClient = BuildDynamoDBClient(region);
            Debug.Log("[AwsManager] Downgraded to guest credentials.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[AwsManager] Downgrade failed: {e.Message}");
        }
    }

    public void RefreshCredentials(string newIdToken)
    {
        try
        {
            _credentials.RemoveLogin(AwsConfig.CognitoProviderName);
            _credentials.AddLogin(AwsConfig.CognitoProviderName, newIdToken);
            _credentials.ClearCredentials();
            Debug.Log("[AwsManager] Credentials refreshed.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[AwsManager] Credential refresh failed: {e.Message}");
        }
    }

    // Builds the DynamoDB client with the SDK's own retries DISABLED. The SDK
    // retries silently by default (~4 attempts w/ backoff) — which is the most
    // likely reason a "single" Query showed as ~11s in logcat with no visible
    // explanation. FurnitureRepository runs its OWN logged retry loop instead
    // (DbWithRetry), so every attempt and its timing appears in the log.
    // Keeps DynamoDB client construction in one place. The AWS SDK's own retries
    // (default Standard mode) are left ON — FurnitureRepository.DbWithRetry logs
    // each call's total elapsed time, so a multi-second call on a small response
    // reveals that the SDK retried internally.
    private AmazonDynamoDBClient BuildDynamoDBClient(RegionEndpoint region)
        => new AmazonDynamoDBClient(_credentials, region);
    
    // Serialises credential refreshes. Without this, 10 concurrent product
    // fetches each recreated the shared _credentials / DynamoDBClient AND fired a
    // Cognito GetCredentialsAsync at the same time — a data race that deadlocked
    // on Android (the editor's different threading hid it). Now only ONE refresh
    // runs at a time and the rest reuse its result.
    private readonly SemaphoreSlim _refreshGate = new SemaphoreSlim(1, 1);
    private DateTime _lastCredsRefreshUtc = DateTime.MinValue;

    // Cognito credentials last ~1h; re-using them well within that is safe and
    // avoids a Cognito round-trip (plus a client rebuild) before every query.
    private const double CredsValidMinutes = 45;

    // Returns true if credentials were actually refreshed (Cognito round-trip +
    // client rebuild); false on the fast path (still fresh) or on error. Callers
    // log this so a category-load trace shows whether a refresh was involved.
    public async Task<bool> RefreshCredentialsIfNeeded()
    {
        // Fast path: still fresh and clients exist → nothing to do.
        if (DynamoDBClient != null &&
            (DateTime.UtcNow - _lastCredsRefreshUtc).TotalMinutes < CredsValidMinutes)
            return false;

        // Single-flight: concurrent callers wait here; only the first refreshes.
        await _refreshGate.WaitAsync();
        try
        {
            // Re-check inside the lock — a caller ahead of us may have just done it.
            if (DynamoDBClient != null &&
                (DateTime.UtcNow - _lastCredsRefreshUtc).TotalMinutes < CredsValidMinutes)
                return false;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var region = RegionEndpoint.GetBySystemName(AwsConfig.Region);

            // Create a completely new credentials object
            // ClearCredentials() does not fix an expired Cognito identity token
            // Only a brand new CognitoAWSCredentials fixes this
            _credentials = new CognitoAWSCredentials(
                AwsConfig.IdentityPoolId,
                region
            );

            // Rebuild clients with the fresh credentials
            S3Client       = new AmazonS3Client(_credentials, region);
            DynamoDBClient = BuildDynamoDBClient(region);

            // Pre-fetch to confirm credentials work before returning
            await _credentials.GetCredentialsAsync();

            _lastCredsRefreshUtc = DateTime.UtcNow;
            sw.Stop();
            Debug.Log($"[AwsManager] Credentials REFRESHED | {sw.ElapsedMilliseconds} ms");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[AwsManager] RefreshCredentials error: {e.Message}");
            return false;
        }
        finally
        {
            _refreshGate.Release();
        }
    }
    
   
}