using System.Threading;
using UnityEngine;
using UnityEngine.UIElements;

public class LoginController : MonoBehaviour, IAuthUI
{
    // UI Elements
    private TextField _emailInput;
    private TextField _passwordInput;
    private Button _loginButton;
    private Button _goToSignUpButton;
    private Button _backToAppButton;
    private Button _forgotPasswordButton;
    private Button _passwordToggle;
    private VisualElement _passwordInputRow;
    private VisualElement _feedbackBanner;
    private Label _feedbackText;
    private VisualElement _loadingOverlay;
    private Label _loadingText;

    // State
    private bool _passwordVisible = false;
    private bool _isInitialized = false;
    private CancellationTokenSource _feedbackCts;

    void Start()
    {
        ScreenNavigator.OnScreenChanged += OnScreenChanged;
        AuthService.OnLoginSuccess += OnLoginSuccess;
        AuthService.OnLoginFailed += OnLoginFailed;
        AuthService.OnLoadingChanged += OnLoadingStateChanged;
    }

    void OnDestroy()
    {
        ScreenNavigator.OnScreenChanged -= OnScreenChanged;
        AuthService.OnLoginSuccess -= OnLoginSuccess;
        AuthService.OnLoginFailed -= OnLoginFailed;
        AuthService.OnLoadingChanged -= OnLoadingStateChanged;
        _feedbackCts?.Cancel();
        _feedbackCts?.Dispose();
        UIManager.OnScreensReady -= OnScreensReady;
    }

    void OnEnable()
    {
        if (UIManager.Instance != null && UIManager.Instance.IsReady)
            Initialize();
        else
            UIManager.OnScreensReady += OnScreensReady;
    }

    void OnDisable()
    {
        UIManager.OnScreensReady -= OnScreensReady;
    }

    private void OnScreensReady()
    {
        UIManager.OnScreensReady -= OnScreensReady;
        Initialize();
    }

    private void Initialize()
    {
        if (_isInitialized)
            return;

        _isInitialized = true;
        QueryElements();
        WireEvents();
        ClearFeedback();
        OnScreenActivated();
    }

    private void OnScreenChanged(ScreenName screen)
    {
        if (screen != ScreenName.Login)
            return;

        QueryElements();
        UnwireEvents();
        WireEvents();
        ClearFeedback();
        OnScreenActivated();
    }

    // IAuthUI implementation
    public void OnScreenActivated()
    {
        ClearInputs();
        PreFillLastEmail();
    }

    private void QueryElements()
    {
        var container = UIManager.Instance.GetScreenContainer(ScreenName.Login);

        if (container == null)
        {
            Debug.LogError("[LoginController] Login container not found.");
            return;
        }

        _emailInput = container.Q<TextField>("EmailInput");
        _passwordInput = container.Q<TextField>("PasswordInput");
        _loginButton = container.Q<Button>("LoginButton");
        _goToSignUpButton = container.Q<Button>("GoToSignUpButton");
        _backToAppButton = container.Q<Button>("BackToAppButton");
        _forgotPasswordButton = container.Q<Button>("ForgotPasswordButton");
        _passwordToggle = container.Q<Button>("PasswordToggle");
        _passwordInputRow = container.Q<VisualElement>("PasswordInputRow");
        _feedbackBanner = container.Q<VisualElement>("FeedbackBanner");
        _feedbackText = container.Q<Label>("FeedbackText");
        _loadingOverlay = container.Q<VisualElement>("LoadingOverlay");
        _loadingText = container.Q<Label>("LoadingText");
    }

    private void UnwireEvents()
    {
        if (_loginButton != null)
            _loginButton.clicked -= OnLoginClicked;

        if (_goToSignUpButton != null)
            _goToSignUpButton.clicked -= OnGoToSignUpClicked;

        if (_backToAppButton != null)
            _backToAppButton.clicked -= OnBackToAppClicked;

        if (_forgotPasswordButton != null)
            _forgotPasswordButton.clicked -= OnForgotPasswordClicked;

        if (_passwordToggle != null)
            _passwordToggle.clicked -= OnPasswordToggleClicked;
    }

    private void WireEvents()
    {
        if (_loginButton != null)
            _loginButton.clicked += OnLoginClicked;

        if (_goToSignUpButton != null)
            _goToSignUpButton.clicked += OnGoToSignUpClicked;

        if (_backToAppButton != null)
            _backToAppButton.clicked += OnBackToAppClicked;

        if (_forgotPasswordButton != null)
            _forgotPasswordButton.clicked += OnForgotPasswordClicked;

        if (_passwordToggle != null)
            _passwordToggle.clicked += OnPasswordToggleClicked;

        if (_passwordInput != null)
        {
            _passwordInput.RegisterCallback<FocusInEvent>(evt =>
                _passwordInputRow?.AddToClassList("input-password-row--focused"));

            _passwordInput.RegisterCallback<FocusOutEvent>(evt =>
                _passwordInputRow?.RemoveFromClassList("input-password-row--focused"));
        }
    }

    // BUTTON HANDLERS

    private async void OnLoginClicked()
    {
        ClearFeedback();
        await AuthService.Instance.Login(
            _emailInput?.value.Trim(),
            _passwordInput?.value
        );
    }

    // Guest-first: leave the auth screens and use the app without signing in.
    private void OnBackToAppClicked()
    {
        ClearFeedback();
        ScreenNavigator.Instance.NavigateTo(ScreenName.MainApp);
    }

    private void OnGoToSignUpClicked()
    {
        ClearFeedback();
        ClearInputs();
        ScreenNavigator.Instance.NavigateTo(ScreenName.SignUp);
    }

    private void OnForgotPasswordClicked()
    {
        ClearFeedback();

        string email = _emailInput?.value.Trim();
        if (!string.IsNullOrEmpty(email))
        {
            PlayerPrefs.SetString("ibm_ros_pending_email", email);
            PlayerPrefs.Save();
        }

        ScreenNavigator.Instance.NavigateTo(ScreenName.ForgotPassword);
    }

    private void OnPasswordToggleClicked()
    {
        _passwordVisible = !_passwordVisible;

        if (_passwordInput != null)
            _passwordInput.isPasswordField = !_passwordVisible;

        if (_passwordToggle != null)
            _passwordToggle.text = _passwordVisible ? "\U0001F440" : "\U0001F441";
    }

    // AUTHSERVICE EVENT HANDLERS

    private void OnLoginSuccess(string message)
    {
        ShowSuccess(message);
        ScreenNavigator.Instance.NavigateTo(ScreenName.MainApp);
    }

    private async void OnLoginFailed(string message, AuthError error)
    {
        ShowError(message);

        switch (error)
        {
            case AuthError.EmailNotConfirmed:
                await System.Threading.Tasks.Task.Delay(2000);
                ScreenNavigator.Instance.NavigateTo(ScreenName.ConfirmEmail);
                break;

            case AuthError.UserNotFound:
                await System.Threading.Tasks.Task.Delay(2000);
                OnGoToSignUpClicked();
                break;
        }
    }

    private void OnLoadingStateChanged(bool isLoading, string message)
    {
        SetLoadingState(isLoading);

        if (_loadingText != null)
            _loadingText.text = message;
    }

    // IAUTHUI IMPLEMENTATION

    public void ShowError(string message)
    {
        _feedbackCts?.Cancel();
        _feedbackCts = new CancellationTokenSource();
        _ = FeedbackHelper.ShowError(
            _feedbackBanner, _feedbackText, message, _feedbackCts.Token);
    }

    public void ShowSuccess(string message)
    {
        _feedbackCts?.Cancel();
        _feedbackCts = new CancellationTokenSource();
        _ = FeedbackHelper.ShowSuccess(
            _feedbackBanner, _feedbackText, message, _feedbackCts.Token);
    }

    public void ClearFeedback()
    {
        _feedbackCts?.Cancel();
        _feedbackCts = null;
        FeedbackHelper.Clear(_feedbackBanner, _feedbackText);
    }

    public void SetLoadingState(bool isLoading)
    {
        if (isLoading)
            _loadingOverlay?.AddToClassList("loading-overlay--visible");
        else
            _loadingOverlay?.RemoveFromClassList("loading-overlay--visible");

        _loginButton?.SetEnabled(!isLoading);
        _emailInput?.SetEnabled(!isLoading);
        _passwordInput?.SetEnabled(!isLoading);
        _forgotPasswordButton?.SetEnabled(!isLoading);
        _goToSignUpButton?.SetEnabled(!isLoading);
    }

    // HELPERS

    private void PreFillLastEmail()
    {
        if (AuthService.Instance == null)
            return;

        string lastEmail = AuthService.Instance.GetLastEmail();

        if (!string.IsNullOrEmpty(lastEmail) && _emailInput != null)
            _emailInput.value = lastEmail;
    }

    private void ClearInputs()
    {
        if (_emailInput != null) _emailInput.value = string.Empty;
        if (_passwordInput != null) _passwordInput.value = string.Empty;
    }
}