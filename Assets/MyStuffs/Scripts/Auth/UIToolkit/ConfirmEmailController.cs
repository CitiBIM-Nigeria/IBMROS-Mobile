using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

public class ConfirmEmailController : MonoBehaviour
{
    // UI Elements
    private TextField _codeBox1;
    private TextField _codeBox2;
    private TextField _codeBox3;
    private TextField _codeBox4;
    private TextField _codeBox5;
    private TextField _codeBox6;
    private Button _confirmButton;
    private Button _resendCodeButton;
    private Button _backToSignUpButton;
    private Label _emailDisplay;
    private Label _codeError;
    private VisualElement _feedbackBanner;
    private Label _feedbackText;
    private Label _countdownText;
    private VisualElement _loadingOverlay;
    private CancellationTokenSource _feedbackCts;

    // All six boxes in order for easy iteration
    private TextField[] _codeBoxes;

    // Resend cooldown state
    private bool _isResendOnCooldown = false;
    private const int ResendCooldownSeconds = 60;

    // Wire events exactly once, and never let two confirm submissions overlap — a
    // double submit re-uses the (now consumed) code and Cognito reports it
    // "expired", bouncing the user back to sign-up.
    private bool _eventsWired  = false;
    // STATIC so the guard holds even if two ConfirmEmailController instances are
    // alive (each would otherwise wire the same button and double-submit — the
    // 2nd submit re-uses the consumed code and Cognito flashes "expired").
    // Reset on entering the screen (ClearInputs / HandleScreenChanged).
    private static bool _isConfirming = false;
    private static bool _confirmed    = false;

    void OnEnable()
    {
        if (UIManager.Instance != null && UIManager.Instance.IsReady)
        {
            QueryElements();
            WireEvents();
            ClearFeedback();
            ClearInputs();
            LoadPendingEmail();
        }
        else
        {
            UIManager.OnScreensReady += OnScreensReady;
        }
    }

    void OnDisable()
    {
        UIManager.OnScreensReady -= OnScreensReady;
    }

    void OnDestroy()
    {
        ScreenNavigator.OnScreenChanged -= HandleScreenChanged;
    }

    // When this screen becomes active, clear any stale code/latch so the user
    // starts fresh (handles signing up again within the same app session).
    private void HandleScreenChanged(ScreenName screen)
    {
        if (screen != ScreenName.ConfirmEmail)
            return;
        ClearFeedback();
        ClearInputs();
        LoadPendingEmail();
    }

    private void OnScreensReady()
    {
        UIManager.OnScreensReady -= OnScreensReady;
        QueryElements();
        WireEvents();
        ClearFeedback();
        ClearInputs();
        LoadPendingEmail();
    }

    private void QueryElements()
    {
        var container = UIManager.Instance.GetScreenContainer(ScreenName.ConfirmEmail);

        if (container == null)
        {
            Debug.LogError("[ConfirmEmailController] ConfirmEmail container not found.");
            return;
        }

        _codeBox1 = container.Q<TextField>("CodeBox1");
        _codeBox2 = container.Q<TextField>("CodeBox2");
        _codeBox3 = container.Q<TextField>("CodeBox3");
        _codeBox4 = container.Q<TextField>("CodeBox4");
        _codeBox5 = container.Q<TextField>("CodeBox5");
        _codeBox6 = container.Q<TextField>("CodeBox6");
        _confirmButton = container.Q<Button>("ConfirmButton");
        _resendCodeButton = container.Q<Button>("ResendCodeButton");
        _backToSignUpButton = container.Q<Button>("BackToSignUpButton");
        _emailDisplay = container.Q<Label>("EmailDisplay");
        _codeError = container.Q<Label>("CodeError");
        _feedbackBanner = container.Q<VisualElement>("FeedbackBanner");
        _feedbackText = container.Q<Label>("FeedbackText");
        _countdownText = container.Q<Label>("CountdownText");
        _loadingOverlay = container.Q<VisualElement>("LoadingOverlay");

        // Store boxes in array for easy iteration
        _codeBoxes = new TextField[]
        {
            _codeBox1, _codeBox2, _codeBox3,
            _codeBox4, _codeBox5, _codeBox6
        };
    }

    private void WireEvents()
    {
        // Guard against re-wiring — OnEnable/OnScreensReady can both run, and
        // re-adding handlers is what caused the double confirm submit.
        if (_eventsWired)
            return;
        _eventsWired = true;

        // Reset the screen (latch + boxes) every time we navigate to it, so a
        // second sign-up in the same session starts clean.
        ScreenNavigator.OnScreenChanged += HandleScreenChanged;

        if (_confirmButton != null)
            _confirmButton.clicked += OnConfirmClicked;

        if (_resendCodeButton != null)
            _resendCodeButton.clicked += OnResendCodeClicked;

        if (_backToSignUpButton != null)
            _backToSignUpButton.clicked += OnBackToSignUpClicked;

        // Wire each code box for auto jump and styling
        for (int i = 0; i < _codeBoxes.Length; i++)
        {
            int index = i;
            var box = _codeBoxes[index];

            if (box == null)
                continue;

            // The UXML caps each box at 1 char, which truncates a pasted code
            // before the handler can spread it. Allow the full 6 so paste works;
            // the handler keeps just one digit per box.
            box.maxLength = 6;

            // Auto jump to next box when a digit is entered
            box.RegisterValueChangedCallback(evt =>
                OnCodeBoxValueChanged(index, evt.newValue));

            // Focus styling
            box.RegisterCallback<FocusInEvent>(evt =>
                SetCodeBoxFocused(index, true));

            box.RegisterCallback<FocusOutEvent>(evt =>
                SetCodeBoxFocused(index, false));

            // Handle backspace ourselves. TrickleDown so we run BEFORE the
            // TextField consumes the key (otherwise the handler never sees it).
            box.RegisterCallback<KeyDownEvent>(evt =>
                OnCodeBoxKeyDown(index, evt), TrickleDown.TrickleDown);
        }
    }

    private void LoadPendingEmail()
    {
        if (_emailDisplay == null)
            return;

        if (AuthService.Instance == null)
            return;

        string pendingEmail = AuthService.Instance.GetPendingEmail();

        _emailDisplay.text = string.IsNullOrEmpty(pendingEmail)
            ? "your email address"
            : AuthService.Instance.GetMaskedEmail(pendingEmail);
    }

    private string MaskEmail(string email)
    {
        if (string.IsNullOrEmpty(email) || !email.Contains("@"))
            return email;

        string[] parts = email.Split('@');
        string localPart = parts[0];
        string domain = parts[1];

        if (localPart.Length <= 2)
            return email;

        // Show first 2 characters then mask the rest before the @
        string visible = localPart.Substring(0, 2);
        string masked = new string('*', localPart.Length - 2);

        return $"{visible}{masked}@{domain}";
    }

    private void OnCodeBoxValueChanged(int index, string newValue)
    {
        var box = _codeBoxes[index];
        if (box == null)
            return;

        if (string.IsNullOrEmpty(newValue))
        {
            box.RemoveFromClassList("confirm-email-code-box--filled");
            return;
        }

        // Keep only the digits from whatever was typed or PASTED.
        string digits = string.Empty;
        foreach (char c in newValue)
            if (char.IsDigit(c))
                digits += c;

        if (digits.Length == 0)
        {
            box.SetValueWithoutNotify(string.Empty);
            return;
        }

        // A paste (or multi-character entry) spreads across the boxes from here on.
        if (digits.Length > 1)
        {
            DistributeDigits(index, digits);
            return;
        }

        // Single digit.
        box.SetValueWithoutNotify(digits);
        box.AddToClassList("confirm-email-code-box--filled");
        box.RemoveFromClassList("confirm-email-code-box--error");

        if (index < _codeBoxes.Length - 1)
            _codeBoxes[index + 1]?.Focus();
        // On the last box, keep focus here (don't jump to the button) so the
        // user can still Backspace to edit the code.
    }

    // Fills boxes from startIndex with the given digits — lets the user paste the
    // whole 6-digit code into any box and have it spread across all of them.
    private void DistributeDigits(int startIndex, string digits)
    {
        int i = startIndex;
        foreach (char d in digits)
        {
            if (i >= _codeBoxes.Length)
                break;
            var b = _codeBoxes[i];
            if (b != null)
            {
                b.SetValueWithoutNotify(d.ToString());
                b.AddToClassList("confirm-email-code-box--filled");
                b.RemoveFromClassList("confirm-email-code-box--error");
            }
            i++;
        }

        if (i < _codeBoxes.Length)
            _codeBoxes[i]?.Focus();
        else
            _codeBoxes[_codeBoxes.Length - 1]?.Focus();
    }

    private void OnCodeBoxKeyDown(int index, KeyDownEvent evt)
    {
        if (evt.keyCode != KeyCode.Backspace)
            return;

        var box = _codeBoxes[index];
        if (box == null)
            return;

        // We fully own Backspace (registered TrickleDown so we run before the
        // TextField eats it). Filled box → clear this digit and stay; empty box →
        // step back and clear the previous one. Repeated Backspace therefore
        // deletes the whole code, one box per press, with no manual re-selecting.
        if (!string.IsNullOrEmpty(box.value))
        {
            box.SetValueWithoutNotify(string.Empty);
            box.RemoveFromClassList("confirm-email-code-box--filled");
            box.RemoveFromClassList("confirm-email-code-box--error");
        }
        else if (index > 0)
        {
            var prev = _codeBoxes[index - 1];
            if (prev != null)
            {
                prev.SetValueWithoutNotify(string.Empty);
                prev.RemoveFromClassList("confirm-email-code-box--filled");
                prev.RemoveFromClassList("confirm-email-code-box--error");
                prev.Focus();
            }
        }

        evt.StopPropagation();
    }

    private void SetCodeBoxFocused(int index, bool focused)
    {
        var box = _codeBoxes[index];

        if (box == null)
            return;

        if (focused)
            box.AddToClassList("confirm-email-code-box--focused");
        else
            box.RemoveFromClassList("confirm-email-code-box--focused");
    }

    private string GetFullCode()
    {
        string code = string.Empty;

        foreach (var box in _codeBoxes)
            code += box?.value ?? string.Empty;

        return code;
    }

    private async void OnConfirmClicked()
    {
        // Already confirmed, or a submit is in flight → ignore. Together these stop
        // the "success then expired bounce" completely.
        if (_confirmed || _isConfirming)
            return;
        _isConfirming = true;

        try
        {
            ClearFeedback();

            string code = GetFullCode();
            string email = PlayerPrefs.GetString("ibm_ros_pending_email", string.Empty);

            string validationError = InputValidator.ValidateConfirmationCode(code);
            if (validationError != null)
            {
                ShowCodeError(validationError);
                SetAllBoxesError();
                return;
            }

            if (string.IsNullOrEmpty(email))
            {
                ShowError("Session expired. Please sign up again.");
                await Task.Delay(2000);
                GoBackToSignUp();
                return;
            }

            SetLoadingState(true);

            AuthResult result = await AuthManager.Instance.ConfirmEmail(email, code);

            SetLoadingState(false);

            if (result.IsSuccess)
            {
                _confirmed = true;   // latch — this screen will not submit again
                PlayerPrefs.DeleteKey("ibm_ros_pending_email");
                PlayerPrefs.Save();

                SetAllBoxesSuccess();
                ShowSuccess(result.Message);

                await Task.Delay(1200);

                // Auto sign-in with the password held from sign-up so the user
                // lands straight in the app. Falls back to the Login screen (email
                // already remembered) if the password isn't available — e.g. the
                // app was restarted between signing up and confirming.
                // NavigateToImmediate avoids the fade transition's in-progress
                // guard silently dropping the navigation.
                string pw = AuthService.Instance?.ConsumeSignupPassword() ?? string.Empty;
                if (!string.IsNullOrEmpty(pw))
                {
                    AuthResult login = await AuthManager.Instance.Login(email, pw);
                    if (login.IsSuccess)
                    {
                        ScreenNavigator.Instance.NavigateToImmediate(ScreenName.MainApp);
                        return;
                    }
                }

                ScreenNavigator.Instance.NavigateToImmediate(ScreenName.Login);
            }
            else
            {
                ShowError(result.Message);
                SetAllBoxesError();

                if (result.Error == AuthError.ExpiredConfirmationCode)
                {
                    await Task.Delay(2000);
                    GoBackToSignUp();
                }
            }
        }
        finally
        {
            _isConfirming = false;
        }
    }

    private async void OnResendCodeClicked()
    {
        if (_isResendOnCooldown)
            return;

        ClearFeedback();

        string email = PlayerPrefs.GetString("ibm_ros_pending_email", string.Empty);

        if (string.IsNullOrEmpty(email))
        {
            ShowError("Session expired. Please sign up again.");
            await Task.Delay(2000);
            GoBackToSignUp();
            return;
        }

        SetLoadingState(true);

        AuthResult result = await AuthManager.Instance.ResendConfirmationCode(email);

        SetLoadingState(false);

        if (result.IsSuccess)
        {
            ShowSuccess("A new verification code has been sent to your email.");
            StartResendCooldown();
        }
        else
        {
            ShowError(result.Message);
        }
    }

    private async void StartResendCooldown()
    {
        _isResendOnCooldown = true;
        _resendCodeButton?.SetEnabled(false);

        _countdownText?.AddToClassList("countdown-text--visible");

        int remaining = ResendCooldownSeconds;

        while (remaining > 0)
        {
            if (_countdownText != null)
                _countdownText.text = $"Resend code in {remaining}s";

            await Task.Delay(1000);
            remaining--;
        }

        _isResendOnCooldown = false;
        _resendCodeButton?.SetEnabled(true);
        _countdownText?.RemoveFromClassList("countdown-text--visible");

        if (_countdownText != null)
            _countdownText.text = string.Empty;
    }

    private void OnBackToSignUpClicked()
    {
        GoBackToSignUp();
    }

    private void GoBackToSignUp()
    {
        ClearFeedback();
        ClearInputs();
        ScreenNavigator.Instance.NavigateTo(ScreenName.SignUp);
    }

    private void SetAllBoxesError()
    {
        foreach (var box in _codeBoxes)
        {
            box?.RemoveFromClassList("confirm-email-code-box--filled");
            box?.RemoveFromClassList("confirm-email-code-box--focused");
            box?.AddToClassList("confirm-email-code-box--error");
        }
    }

    private void SetAllBoxesSuccess()
    {
        foreach (var box in _codeBoxes)
        {
            box?.RemoveFromClassList("confirm-email-code-box--filled");
            box?.RemoveFromClassList("confirm-email-code-box--error");
            box?.AddToClassList("confirm-email-code-box--success");
        }
    }

    private void SetLoadingState(bool isLoading)
    {
        if (isLoading)
            _loadingOverlay?.AddToClassList("loading-overlay--visible");
        else
            _loadingOverlay?.RemoveFromClassList("loading-overlay--visible");

        _confirmButton?.SetEnabled(!isLoading);
        _resendCodeButton?.SetEnabled(!isLoading && !_isResendOnCooldown);
        _backToSignUpButton?.SetEnabled(!isLoading);

        foreach (var box in _codeBoxes)
            box?.SetEnabled(!isLoading);
    }

    private void ShowError(string message)
    {
        _feedbackCts?.Cancel();
        _feedbackCts = new CancellationTokenSource();
        _ = FeedbackHelper.ShowError(_feedbackBanner, _feedbackText, message, _feedbackCts.Token);
    }

    private void ShowSuccess(string message)
    {
        _feedbackCts?.Cancel();
        _feedbackCts = new CancellationTokenSource();
        _ = FeedbackHelper.ShowSuccess(_feedbackBanner, _feedbackText, message, _feedbackCts.Token);
    }

    private void ClearFeedback()
    {
        _feedbackCts?.Cancel();
        _feedbackCts = null;
        FeedbackHelper.Clear(_feedbackBanner, _feedbackText);
    }

    private void ShowCodeError(string message)
    {
        if (_codeError == null)
            return;

        _codeError.text = message;
        _codeError.AddToClassList("input-error-text--visible");
    }

    private void ClearInputs()
    {
        // Fresh code-entry session → release the latches so the user can submit.
        _confirmed    = false;
        _isConfirming = false;

        if (_codeBoxes == null)
            return;

        foreach (var box in _codeBoxes)
        {
            if (box == null)
                continue;

            box.SetValueWithoutNotify(string.Empty);
            box.RemoveFromClassList("confirm-email-code-box--filled");
            box.RemoveFromClassList("confirm-email-code-box--error");
            box.RemoveFromClassList("confirm-email-code-box--success");
            box.RemoveFromClassList("confirm-email-code-box--focused");
        }
    }
}