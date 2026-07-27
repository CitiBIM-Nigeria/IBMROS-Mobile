using System;
using UnityEngine;
using UnityEngine.UIElements;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }
    // Add this line near the top with other fields
    public static event Action OnScreensReady;
    
    public bool IsReady { get; private set; } = false;

    [SerializeField] private UIDocument uiDocument;

    // Drag each UXML asset into these slots in the Inspector
    [Header("Screen Assets")]
    [SerializeField] private VisualTreeAsset splashScreenAsset;
    [SerializeField] private VisualTreeAsset loginScreenAsset;
    [SerializeField] private VisualTreeAsset signUpScreenAsset;
    [SerializeField] private VisualTreeAsset confirmEmailScreenAsset;
    [SerializeField] private VisualTreeAsset forgotPasswordScreenAsset;
    [SerializeField] private VisualTreeAsset confirmPasswordScreenAsset;
    [SerializeField] private VisualTreeAsset mainAppScreenAsset;
    [SerializeField] private VisualTreeAsset deleteAccountScreenAsset;
    [SerializeField] private VisualTreeAsset presetPickerScreenAsset;

    // Screen container references
    private VisualElement _splashScreenContainer;
    private VisualElement _loginScreenContainer;
    private VisualElement _signUpScreenContainer;
    private VisualElement _confirmEmailScreenContainer;
    private VisualElement _forgotPasswordScreenContainer;
    private VisualElement _confirmPasswordScreenContainer;
    private VisualElement _mainAppScreenContainer;
    private VisualElement _deleteAccountScreenContainer;
    private VisualElement _presetPickerScreenContainer;

    // Global overlay references
    private VisualElement _globalLoadingOverlay;
    private VisualElement _globalNoConnectionBanner;
    private Label _globalLoadingText;
    private Button _globalRetryButton;

    public VisualElement Root { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    void OnEnable()
    {
        Root = uiDocument.rootVisualElement;
        QueryContainers();
        InjectScreens();
        QueryGlobalElements();
    }

    private void QueryContainers()
    {
        _splashScreenContainer = Root.Q<VisualElement>("SplashScreenContainer");
        _loginScreenContainer = Root.Q<VisualElement>("LoginScreenContainer");
        _signUpScreenContainer = Root.Q<VisualElement>("SignUpScreenContainer");
        _confirmEmailScreenContainer = Root.Q<VisualElement>("ConfirmEmailScreenContainer");
        _forgotPasswordScreenContainer = Root.Q<VisualElement>("ForgotPasswordScreenContainer");
        _confirmPasswordScreenContainer = Root.Q<VisualElement>("ConfirmPasswordScreenContainer");
        _mainAppScreenContainer = Root.Q<VisualElement>("MainAppScreenContainer");
        _deleteAccountScreenContainer = Root.Q<VisualElement>("DeleteAccountScreenContainer");
        _presetPickerScreenContainer = Root.Q<VisualElement>("PresetPickerScreenContainer");
    }

    private void InjectScreens()
    {
        InjectScreen(splashScreenAsset, _splashScreenContainer, "SplashScreen");
        InjectScreen(loginScreenAsset, _loginScreenContainer, "LoginScreen");
        InjectScreen(signUpScreenAsset, _signUpScreenContainer, "SignUpScreen");
        InjectScreen(confirmEmailScreenAsset, _confirmEmailScreenContainer, "ConfirmEmailScreen");
        InjectScreen(forgotPasswordScreenAsset, _forgotPasswordScreenContainer, "ForgotPasswordScreen");
        InjectScreen(confirmPasswordScreenAsset, _confirmPasswordScreenContainer, "ConfirmPasswordScreen");
        InjectScreen(mainAppScreenAsset, _mainAppScreenContainer, "MainAppScreen");
        InjectScreen(deleteAccountScreenAsset, _deleteAccountScreenContainer, "DeleteAccountScreen");
        InjectScreen(presetPickerScreenAsset, _presetPickerScreenContainer, "PresetPickerScreen");

        IsReady = true;
        Debug.Log("[UIManager] All screens ready.");

        // Hide splash before firing OnScreensReady so it never renders
        if (SceneTransition.SkipSplash)
        {
            var splashContainer = GetScreenContainer(ScreenName.Splash);
            if (splashContainer != null)
                splashContainer.style.display = DisplayStyle.None;

            Debug.Log("[UIManager] Splash hidden for room return.");
        }

        OnScreensReady?.Invoke();
    }

    private void InjectScreen(VisualTreeAsset asset, VisualElement container, string screenName)
    {
        if (asset == null)
        {
            Debug.LogError($"[UIManager] {screenName} asset is not assigned in Inspector.");
            return;
        }

        if (container == null)
        {
            Debug.LogError($"[UIManager] {screenName} container not found in UXML.");
            return;
        }

        // CloneTree injects the UXML content directly into the container
        asset.CloneTree(container);
        Debug.Log($"[UIManager] Injected {screenName} successfully.");
    }

    private void QueryGlobalElements()
    {
        _globalLoadingOverlay = Root.Q<VisualElement>("GlobalLoadingOverlay");
        _globalNoConnectionBanner = Root.Q<VisualElement>("GlobalNoConnectionBanner");
        _globalLoadingText = Root.Q<Label>("GlobalLoadingText");
        _globalRetryButton = Root.Q<Button>("GlobalRetryButton");

        if (_globalRetryButton != null)
            _globalRetryButton.clicked += OnGlobalRetryClicked;
    }

    public VisualElement GetScreenContainer(ScreenName screen)
    {
        switch (screen)
        {
            case ScreenName.Splash: return _splashScreenContainer;
            case ScreenName.Login: return _loginScreenContainer;
            case ScreenName.SignUp: return _signUpScreenContainer;
            case ScreenName.ConfirmEmail: return _confirmEmailScreenContainer;
            case ScreenName.ForgotPassword: return _forgotPasswordScreenContainer;
            case ScreenName.ConfirmPassword: return _confirmPasswordScreenContainer;
            case ScreenName.MainApp: return _mainAppScreenContainer;
            case ScreenName.DeleteAccount: return _deleteAccountScreenContainer;
            case ScreenName.PresetPicker: return _presetPickerScreenContainer;
            default: return null;
        }
    }

    public void ShowGlobalLoading(string message = "Please wait...")
    {
        if (_globalLoadingText != null)
            _globalLoadingText.text = message;

        _globalLoadingOverlay?.AddToClassList("loading-overlay--visible");
    }

    public void HideGlobalLoading()
    {
        _globalLoadingOverlay?.RemoveFromClassList("loading-overlay--visible");
    }

    public void ShowNoConnectionBanner()
    {
        _globalNoConnectionBanner?.AddToClassList("no-connection-banner--visible");
    }

    public void HideNoConnectionBanner()
    {
        _globalNoConnectionBanner?.RemoveFromClassList("no-connection-banner--visible");
    }

    private void OnGlobalRetryClicked()
    {
        bool isConnected = Application.internetReachability
            != NetworkReachability.NotReachable;

        if (isConnected)
        {
            HideNoConnectionBanner();
            NetworkMonitor.Instance.StartMonitoring();
        }
    }

    void OnDestroy()
    {
        if (_globalRetryButton != null)
            _globalRetryButton.clicked -= OnGlobalRetryClicked;
    }
}
// All screen names used across the app
public enum ScreenName
{
    Splash,
    Login,
    SignUp,
    ConfirmEmail,
    ForgotPassword,
    ConfirmPassword,
    MainApp,
    DeleteAccount,
    PresetPicker
}