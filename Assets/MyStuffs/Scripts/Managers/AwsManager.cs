using System;
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
            DynamoDBClient = new AmazonDynamoDBClient(_credentials, region);

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
            DynamoDBClient = new AmazonDynamoDBClient(_credentials, region);
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
            DynamoDBClient = new AmazonDynamoDBClient(_credentials, region);
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
    
    public async Task RefreshCredentialsIfNeeded()
    {
        try
        {
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
            DynamoDBClient = new AmazonDynamoDBClient(_credentials, region);

            // If user is authenticated, re-add their login token
            // so they do not lose their authenticated session
            // (skip this block if you only use guest access)
            // _credentials.AddLogin(AwsConfig.CognitoProviderName, savedIdToken);

            // Pre-fetch to confirm credentials work before returning
            await _credentials.GetCredentialsAsync();

            Debug.Log("[AwsManager] Credentials refreshed successfully.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[AwsManager] RefreshCredentials error: {e.Message}");
        }
    }
    
   
}