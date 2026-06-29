using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

public class SignUpController : MonoBehaviour, IAuthUI
{
    // UI Elements
    private TextField _emailInput;
    private TextField _passwordInput;
    private TextField _confirmPasswordInput;
    private Button _signUpButton;
    private Button _goToLoginButton;
    private Button _passwordToggle;
    private Button _confirmPasswordToggle;
    private VisualElement _passwordInputRow;
    private VisualElement _confirmPasswordInputRow;
    private VisualElement _feedbackBanner;
    private Label _feedbackText;
    private VisualElement _loadingOverlay;
    private Label _loadingText;

    // Password requirement labels and icons
    private Label _req1Icon;
    private Label _req1Text;
    private Label _req2Icon;
    private Label _req2Text;
    private Label _req3Icon;
    private Label _req3Text;
    private Label _req4Icon;
    private Label _req4Text;

    // Password strength elements
    private VisualElement _strengthFill;
    private Label _strengthLabel;

    // State
    private bool _passwordVisible = false;
    private bool _confirmPasswordVisible = false;
    private CancellationTokenSource _feedbackCts;

    void Start()
    {
        AuthService.OnSignUpSuccess += OnSignUpSuccess;
        AuthService.OnSignUpFailed += OnSignUpFailed;
        AuthService.OnLoadingChanged += OnLoadingStateChanged;
    }

    void OnDestroy()
    {
        AuthService.OnSignUpSuccess -= OnSignUpSuccess;
        AuthService.OnSignUpFailed -= OnSignUpFailed;
        AuthService.OnLoadingChanged -= OnLoadingStateChanged;
        _feedbackCts?.Cancel();
        _feedbackCts?.Dispose();
        UIManager.OnScreensReady -= OnScreensReady;
    }

    void OnEnable()
    {
        // Clear the form every time this screen becomes active (not just the
        // first time) so old email/password values never linger.
        ScreenNavigator.OnScreenChanged -= OnScreenChanged;
        ScreenNavigator.OnScreenChanged += OnScreenChanged;

        if (UIManager.Instance != null && UIManager.Instance.IsReady)
        {
            QueryElements();
            WireEvents();
            ClearFeedback();
            OnScreenActivated();
        }
        else
        {
            UIManager.OnScreensReady += OnScreensReady;
        }
    }

    void OnDisable()
    {
        UIManager.OnScreensReady -= OnScreensReady;
        ScreenNavigator.OnScreenChanged -= OnScreenChanged;
    }

    private void OnScreenChanged(ScreenName screen)
    {
        if (screen != ScreenName.SignUp)
            return;
        ClearFeedback();
        OnScreenActivated();   // clears email / password / confirm fields
    }

    private void OnScreensReady()
    {
        UIManager.OnScreensReady -= OnScreensReady;
        QueryElements();
        WireEvents();
        ClearFeedback();
        OnScreenActivated();
    }

    // IAuthUI implementation
    public void OnScreenActivated()
    {
        ClearInputs();
    }

    private void QueryElements()
    {
        var container = UIManager.Instance.GetScreenContainer(ScreenName.SignUp);

        if (container == null)
        {
            Debug.LogError("[SignUpController] SignUp container not found.");
            return;
        }

        _emailInput = container.Q<TextField>("EmailInput");
        _passwordInput = container.Q<TextField>("PasswordInput");
        _confirmPasswordInput = container.Q<TextField>("ConfirmPasswordInput");
        _signUpButton = container.Q<Button>("SignUpButton");
        _goToLoginButton = container.Q<Button>("GoToLoginButton");
        _passwordToggle = container.Q<Button>("PasswordToggle");
        _confirmPasswordToggle = container.Q<Button>("ConfirmPasswordToggle");
        _passwordInputRow = container.Q<VisualElement>("PasswordInputRow");
        _confirmPasswordInputRow = container.Q<VisualElement>("ConfirmPasswordInputRow");
        _feedbackBanner = container.Q<VisualElement>("FeedbackBanner");
        _feedbackText = container.Q<Label>("FeedbackText");
        _loadingOverlay = container.Q<VisualElement>("LoadingOverlay");
        _loadingText = container.Q<Label>("LoadingText");

        _req1Icon = container.Q<Label>("Req1Icon");
        _req1Text = container.Q<Label>("Req1Text");
        _req2Icon = container.Q<Label>("Req2Icon");
        _req2Text = container.Q<Label>("Req2Text");
        _req3Icon = container.Q<Label>("Req3Icon");
        _req3Text = container.Q<Label>("Req3Text");
        _req4Icon = container.Q<Label>("Req4Icon");
        _req4Text = container.Q<Label>("Req4Text");

        _strengthFill = container.Q<VisualElement>("StrengthFill");
        _strengthLabel = container.Q<Label>("StrengthLabel");
    }

    private void WireEvents()
    {
        if (_signUpButton != null)
            _signUpButton.clicked += OnSignUpClicked;

        if (_goToLoginButton != null)
            _goToLoginButton.clicked += OnGoToLoginClicked;

        var backButton = UIManager.Instance
            .GetScreenContainer(ScreenName.SignUp)
            ?.Q<Button>("BackToLoginButton");

        if (backButton != null)
            backButton.clicked += OnGoToLoginClicked;

        if (_passwordToggle != null)
            _passwordToggle.clicked += OnPasswordToggleClicked;

        if (_confirmPasswordToggle != null)
            _confirmPasswordToggle.clicked += OnConfirmPasswordToggleClicked;

        if (_passwordInput != null)
        {
            _passwordInput.RegisterCallback<FocusInEvent>(evt =>
                _passwordInputRow?.AddToClassList("input-password-row--focused"));

            _passwordInput.RegisterCallback<FocusOutEvent>(evt =>
                _passwordInputRow?.RemoveFromClassList("input-password-row--focused"));

            _passwordInput.RegisterValueChangedCallback(evt =>
                UpdatePasswordStrength(evt.newValue));
        }

        if (_confirmPasswordInput != null)
        {
            _confirmPasswordInput.RegisterCallback<FocusInEvent>(evt =>
                _confirmPasswordInputRow?.AddToClassList("input-password-row--focused"));

            _confirmPasswordInput.RegisterCallback<FocusOutEvent>(evt =>
                _confirmPasswordInputRow?.RemoveFromClassList("input-password-row--focused"));
        }
    }

    // BUTTON HANDLERS

    private async void OnSignUpClicked()
    {
        ClearFeedback();
        await AuthService.Instance.SignUp(
            _emailInput?.value.Trim(),
            _passwordInput?.value,
            _confirmPasswordInput?.value
        );
    }

    private void OnGoToLoginClicked()
    {
        ClearFeedback();
        ClearInputs();
        ScreenNavigator.Instance.NavigateTo(ScreenName.Login);
    }

    private void OnPasswordToggleClicked()
    {
        _passwordVisible = !_passwordVisible;

        if (_passwordInput != null)
            _passwordInput.isPasswordField = !_passwordVisible;

        if (_passwordToggle != null)
            _passwordToggle.text = _passwordVisible ? "\U0001F440" : "\U0001F441";
    }

    private void OnConfirmPasswordToggleClicked()
    {
        _confirmPasswordVisible = !_confirmPasswordVisible;

        if (_confirmPasswordInput != null)
            _confirmPasswordInput.isPasswordField = !_confirmPasswordVisible;

        if (_confirmPasswordToggle != null)
            _confirmPasswordToggle.text = _confirmPasswordVisible
                ? "\U0001F440"
                : "\U0001F441";
    }

    // AUTHSERVICE EVENT HANDLERS

    private async void OnSignUpSuccess(string message)
    {
        ShowSuccess(message);
        await Task.Delay(1500);
        ScreenNavigator.Instance.NavigateTo(ScreenName.ConfirmEmail);
    }

    private async void OnSignUpFailed(string message, AuthError error)
    {
        ShowError(message);

        if (error == AuthError.EmailAlreadyExists)
        {
            await Task.Delay(2000);
            OnGoToLoginClicked();
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

        _signUpButton?.SetEnabled(!isLoading);
        _emailInput?.SetEnabled(!isLoading);
        _passwordInput?.SetEnabled(!isLoading);
        _confirmPasswordInput?.SetEnabled(!isLoading);
    }

    // PASSWORD STRENGTH

    private void UpdatePasswordStrength(string password)
    {
        bool hasLength = password.Length >= 8;
        bool hasUpper = Regex.IsMatch(password, @"[A-Z]");
        bool hasDigit = Regex.IsMatch(password, @"\d");
        bool hasSpecial = Regex.IsMatch(password, @"[!@#$%^&*(),.?""':{}|<>]");

        SetRequirementMet(_req1Icon, _req1Text, hasLength);
        SetRequirementMet(_req2Icon, _req2Text, hasUpper);
        SetRequirementMet(_req3Icon, _req3Text, hasDigit);
        SetRequirementMet(_req4Icon, _req4Text, hasSpecial);

        int score = 0;
        if (hasLength) score++;
        if (hasUpper) score++;
        if (hasDigit) score++;
        if (hasSpecial) score++;

        UpdateStrengthBar(score);
    }

    private void SetRequirementMet(Label icon, Label text, bool met)
    {
        if (icon == null || text == null)
            return;

        if (met)
        {
            icon.text = "\u2713";
            icon.AddToClassList("signup-req-icon--met");
            text.AddToClassList("signup-req-text--met");
        }
        else
        {
            icon.text = "\u25CF";
            icon.RemoveFromClassList("signup-req-icon--met");
            text.RemoveFromClassList("signup-req-text--met");
        }
    }

    private void UpdateStrengthBar(int score)
    {
        if (_strengthFill == null || _strengthLabel == null)
            return;

        _strengthFill.RemoveFromClassList("signup-strength-fill--weak");
        _strengthFill.RemoveFromClassList("signup-strength-fill--fair");
        _strengthFill.RemoveFromClassList("signup-strength-fill--good");
        _strengthFill.RemoveFromClassList("signup-strength-fill--strong");

        _strengthLabel.RemoveFromClassList("signup-strength-label--weak");
        _strengthLabel.RemoveFromClassList("signup-strength-label--fair");
        _strengthLabel.RemoveFromClassList("signup-strength-label--good");
        _strengthLabel.RemoveFromClassList("signup-strength-label--strong");

        switch (score)
        {
            case 1:
                _strengthFill.AddToClassList("signup-strength-fill--weak");
                _strengthLabel.AddToClassList("signup-strength-label--weak");
                _strengthLabel.text = "WEAK";
                break;
            case 2:
                _strengthFill.AddToClassList("signup-strength-fill--fair");
                _strengthLabel.AddToClassList("signup-strength-label--fair");
                _strengthLabel.text = "FAIR";
                break;
            case 3:
                _strengthFill.AddToClassList("signup-strength-fill--good");
                _strengthLabel.AddToClassList("signup-strength-label--good");
                _strengthLabel.text = "GOOD";
                break;
            case 4:
                _strengthFill.AddToClassList("signup-strength-fill--strong");
                _strengthLabel.AddToClassList("signup-strength-label--strong");
                _strengthLabel.text = "STRONG";
                break;
            default:
                _strengthLabel.text = string.Empty;
                break;
        }
    }

    // HELPERS

    private void ClearInputs()
    {
        if (_emailInput != null) _emailInput.value = string.Empty;
        if (_passwordInput != null) _passwordInput.value = string.Empty;
        if (_confirmPasswordInput != null) _confirmPasswordInput.value = string.Empty;

        if (_strengthFill != null)
        {
            _strengthFill.RemoveFromClassList("signup-strength-fill--weak");
            _strengthFill.RemoveFromClassList("signup-strength-fill--fair");
            _strengthFill.RemoveFromClassList("signup-strength-fill--good");
            _strengthFill.RemoveFromClassList("signup-strength-fill--strong");
        }

        if (_strengthLabel != null)
            _strengthLabel.text = string.Empty;

        SetRequirementMet(_req1Icon, _req1Text, false);
        SetRequirementMet(_req2Icon, _req2Text, false);
        SetRequirementMet(_req3Icon, _req3Text, false);
        SetRequirementMet(_req4Icon, _req4Text, false);
    }
}