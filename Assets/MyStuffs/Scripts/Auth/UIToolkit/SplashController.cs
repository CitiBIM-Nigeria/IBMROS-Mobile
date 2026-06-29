using System.Threading.Tasks;
using UnityEngine;

public class SplashController : MonoBehaviour
{
    private const int MinSplashDurationMs = 2000;
    private bool _awsReady = false;
    private bool _splashTimerDone = false;
    private bool _hasNavigated = false;

    void OnEnable()
    {
        // SceneEntryPoint handles SkipSplash. We just bail out so we do not race.
        if (SceneTransition.SkipSplash)
        {
            _hasNavigated = true;
            return;
        }

        AuthService.OnSessionRestored += OnSessionRestored;
        AuthService.OnSessionExpiredOrInvalid += OnSessionExpiredOrInvalid;
        AwsManager.OnAwsReady += OnAwsReady;
    }

    void Start()
    {
        if (_hasNavigated)
            return;

        // SceneEntryPoint already handled navigation, do not run splash flow
        if (ScreenNavigator.Instance != null && ScreenNavigator.Instance.HasBeenNavigated)
            return;
        
        StartSplashTimer();

        if (AwsManager.Instance != null && AwsManager.Instance.IsInitialized)
        {
           _awsReady = true;
            TryCheckSession();
        }
    }

    private void OnScreensReadySkip()
    {
        UIManager.OnScreensReady -= OnScreensReadySkip;
        ScreenNavigator.Instance.NavigateTo(ScreenName.MainApp);
    }

    void OnDisable()
    {
        AuthService.OnSessionRestored -= OnSessionRestored;
        AuthService.OnSessionExpiredOrInvalid -= OnSessionExpiredOrInvalid;
        AwsManager.OnAwsReady -= OnAwsReady;
    }
    private async void StartSplashTimer()
    {
        await Task.Delay(MinSplashDurationMs);
        _splashTimerDone = true;
        TryCheckSession();
    }

    private void OnAwsReady()
    {
        AwsManager.OnAwsReady -= OnAwsReady;
        _awsReady = true;
        TryCheckSession();
    }

    private async void TryCheckSession()
    {
        if (!_awsReady || !_splashTimerDone)
            return;

        if (_hasNavigated)
            return;

        if (AuthService.Instance == null)
            return;

        Debug.Log("[SplashController] Both conditions met. Checking session.");

        // Do NOT set _hasNavigated here
        // Let OnSessionRestored and OnSessionExpiredOrInvalid set it
        await AuthService.Instance.CheckSession();
    }

    private void OnSessionRestored()
    {
        if (_hasNavigated)
            return;
        _hasNavigated = true;

        if (ScreenNavigator.Instance == null)
        {
            Debug.LogError("[SplashController] ScreenNavigator.Instance is null.");
            return;
        }

        Debug.Log("[SplashController] Navigating to MainApp.");
        ScreenNavigator.Instance.NavigateTo(ScreenName.MainApp);
    }

    private void OnSessionExpiredOrInvalid()
    {
        if (_hasNavigated)
        {
            Debug.Log("[SplashController] Already navigated. Skipping.");
            return;
        }

        _hasNavigated = true;

        if (ScreenNavigator.Instance == null)
        {
            Debug.LogError("[SplashController] ScreenNavigator.Instance is null.");
            return;
        }

        // Guest-first: no valid session → go straight into the app as a guest
        // (AwsManager already holds guest credentials). Sign-in is offered inside
        // the app and only required for premium actions — not as an entry wall.
        Debug.Log("[SplashController] No session — entering as guest.");
        ScreenNavigator.Instance.NavigateTo(ScreenName.MainApp);
    }
    
    
}