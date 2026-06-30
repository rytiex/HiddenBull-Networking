using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

using JetBrains.Annotations;
using System.Globalization;
using LMirman.VespaIO;

using HiddenBull.Console.Commands;
using UnityEngine;
using TMPro;

namespace HiddenBull.Console
{
    /// <summary>
    /// Default MonoBehaviour that interacts with the <see cref="DevConsole"/> and manages the canvas state of the UI.
    /// </summary>
    [PublicAPI, RequireComponent(typeof(NetworkCommandBridge))]
    public class Terminal : MonoBehaviour
    {
        public static Terminal Instance { get; private set; }

        [Header("Component References")]
        [SerializeField] private Canvas canvas;
        [SerializeField] private GameObject container;
        [SerializeField] private TextMeshProUGUI history;
        [SerializeField] private TMP_InputField inputText;
        [SerializeField] private TMP_Text autofillText;
        [SerializeField] private RectTransform autofillContainer;
        [SerializeField] private RectTransform autofillParent;

        private bool historyDirty;
        private bool hasNoEventSystem;
        private GameObject previousSelectable;
        private int recentInputIndex = -1;
        private HistoryInput historyInput;
        private float historyInputTime;
        private AutofillValue autofillPreviewValue;

        private void Awake()
        {
            Instance = this;

            if (!Networking.Steam.SteamInformation.IsDedicated)
                Application.logMessageReceived += ApplicationOnLogMessageReceived;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        }
        private void OnDestroy() =>
            Application.logMessageReceived -= ApplicationOnLogMessageReceived;
        private void OnEnable()
        {
            SceneManager.activeSceneChanged += SceneManagerOnActiveSceneChanged;
            DevConsole.console.OutputUpdate += ConsoleOnOutputUpdate;
            inputText.onValueChanged.AddListener(InputText_OnValueChanged);
            recentInputIndex = -1;
        }
        private void OnDisable()
        {
            SceneManager.activeSceneChanged -= SceneManagerOnActiveSceneChanged;
            DevConsole.console.OutputUpdate -= ConsoleOnOutputUpdate;
            inputText.onValueChanged.RemoveListener(InputText_OnValueChanged);
        }

        private void SceneManagerOnActiveSceneChanged(Scene prevScene, Scene currScene)
        {
            if (DevConsole.ConsoleActive && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(inputText.gameObject);
        }
        private void ApplicationOnLogMessageReceived(string condition, string stacktrace, LogType type) =>
            DevConsole.Log(condition, type switch
            {
                LogType.Error => LMirman.VespaIO.Console.LogStyling.Error,
                LogType.Assert => LMirman.VespaIO.Console.LogStyling.Assert,
                LogType.Warning => LMirman.VespaIO.Console.LogStyling.Warning,
                LogType.Log => LMirman.VespaIO.Console.LogStyling.Info,
                LogType.Exception => LMirman.VespaIO.Console.LogStyling.Exception,
                _ => LMirman.VespaIO.Console.LogStyling.Plain,
            });
        private void ConsoleOnOutputUpdate() =>
            historyDirty = true;
        private void InputText_OnValueChanged(string value)
        {
            recentInputIndex = -1;
            DevConsole.console.VirtualText = value;
        }

        private void Start()
        {
            SetConsoleState(false);
            inputText.onSubmit.AddListener(OnSubmit);
            historyDirty = true;
        }
        private void Update()
        {
            bool activeKey = ConsoleInput.GetButtonDown("DeveloperConsoleActive");
            bool autofillKey = ConsoleInput.GetButtonDown("DeveloperConsoleFill");

            UpdateTraverseCommandHistory();
            UpdateAutofillPreview();

            // Auto fill
            if (DevConsole.ConsoleActive && autofillKey && DevConsole.console.TryGetNextAutofillApplied(out string newInputText))
            {
                inputText.SetTextWithoutNotify(newInputText);
                inputText.caretPosition = inputText.text.Length;
            }

            // Change active state
            if (activeKey)
                if (DevConsole.ConsoleActive || !DevConsole.console.Enabled)
                    SetConsoleState(false);
                else if (!DevConsole.ConsoleActive)
                    SetConsoleState(true);

            // Update output
            if (historyDirty)
            {
                history.text = DevConsole.console.GetOutputLog();
                historyDirty = false;
            }

            void UpdateTraverseCommandHistory()
            {
                const string historyUpName = "DeveloperConsoleHistoryUp";
                const string historyDownName = "DeveloperConsoleHistoryDown";

                bool historyUp = ConsoleInput.GetButtonDown(historyUpName);
                bool historyDown = ConsoleInput.GetButtonDown(historyDownName);

                // Traverse command history on up/down arrow key down.
                if (DevConsole.ConsoleActive && historyUp)
                {
                    SetRecentInput(1);
                    historyInput = HistoryInput.Up;
                    historyInputTime = 0;
                }
                else if (DevConsole.ConsoleActive && historyDown)
                {
                    SetRecentInput(-1);
                    historyInput = HistoryInput.Down;
                    historyInputTime = 0;
                }

                // Held key scroll
                if (historyInput == HistoryInput.Up)
                {
                    Scroll(1, historyUpName);
                }
                else if (historyInput == HistoryInput.Down)
                {
                    Scroll(-1, historyDownName);
                }

                void Scroll(int direction, string stopKeyCode)
                {
                    historyInputTime += Time.unscaledDeltaTime;
                    if (historyInputTime > .5f)
                    {
                        SetRecentInput(direction);
                        historyInputTime -= .15f;
                    }

                    if (ConsoleInput.GetButtonUp(stopKeyCode) || recentInputIndex == -1 || recentInputIndex == DevConsole.console.recentInputs.Count - 1)
                    {
                        historyInput = HistoryInput.None;
                        historyInputTime = 0;
                    }
                }
            }

            void UpdateAutofillPreview()
            {
                if (autofillPreviewValue == DevConsole.console.NextAutofill)
                {
                    return;
                }

                autofillPreviewValue = DevConsole.console.NextAutofill;
                if (autofillPreviewValue != null && DevConsole.console.VirtualText.Length > 0)
                {
                    TMP_CharacterInfo startIndexInfo = inputText.textComponent.textInfo.characterInfo[autofillPreviewValue.globalStartIndex];
                    autofillText.text = autofillPreviewValue.markupNewWord;
                    autofillContainer.gameObject.SetActive(true);
                    autofillContainer.sizeDelta = autofillText.GetPreferredValues(1024, autofillContainer.sizeDelta.y);
                    float parentHalfWidth = autofillParent.rect.width / 2;
                    float max = parentHalfWidth - autofillContainer.sizeDelta.x;
                    float autofillHorizontal = Mathf.Clamp(startIndexInfo.topLeft.x, -parentHalfWidth, max);
                    autofillContainer.anchoredPosition = new Vector2(autofillHorizontal, 32);
                }
                else
                {
                    autofillContainer.gameObject.SetActive(false);
                    autofillText.text = string.Empty;
                }
            }
        }

        private void OnSubmit(string submitText)
        {
            if (DevConsole.ConsoleActive && !string.IsNullOrWhiteSpace(submitText))
            {
                inputText.SetTextWithoutNotify(string.Empty);
                DevConsole.console.VirtualText = string.Empty;
                DevConsole.console.RunInput(submitText);
                EventSystem.current.SetSelectedGameObject(inputText.gameObject);
                inputText.OnPointerClick(new PointerEventData(EventSystem.current));
                recentInputIndex = -1;
            }
        }
        public void CallCommand(string command)
        {
            if (!string.IsNullOrWhiteSpace(command))
                DevConsole.console.RunInput(command, true);
        }

        public void SetConsoleState(bool value)
        {
            DevConsole.ConsoleActive = value;
            if (container)
                container.SetActive(value);

            canvas.enabled = value;
            inputText.enabled = value;
            inputText.SetTextWithoutNotify(string.Empty);
            DevConsole.console.VirtualText = string.Empty;
            recentInputIndex = -1;
            if (EventSystem.current != null)
            {
                if (value)
                {
                    previousSelectable = EventSystem.current.currentSelectedGameObject;
                    EventSystem.current.SetSelectedGameObject(inputText.gameObject);
                    inputText.OnPointerClick(new PointerEventData(EventSystem.current));
                }
                else if (previousSelectable != null && previousSelectable.activeInHierarchy)
                {
                    EventSystem.current.SetSelectedGameObject(previousSelectable);
                    previousSelectable = null;
                }

                // Clear no event system warning if present since one is present now.
                if (hasNoEventSystem)
                {
                    DevConsole.console.Clear();
                    hasNoEventSystem = false;
                }
            }
#if UNITY_EDITOR
            else
            {
                DevConsole.console.Clear();
                DevConsole.Log("No event system present in scene. The developer console cannot function without this.", LMirman.VespaIO.Console.LogStyling.Error);
                DevConsole.Log("Add an event system by right clicking in the scene Hierarchy > UI > Event System.");
                hasNoEventSystem = true;
            }
#endif
        }
        private void SetRecentInput(int direction)
        {
            recentInputIndex = Mathf.Clamp(recentInputIndex + direction, -1, DevConsole.console.recentInputs.Count - 1);
            string recentInput = DevConsole.console.GetRecentInputByIndex(recentInputIndex);
            DevConsole.console.VirtualText = recentInput;
            inputText.SetTextWithoutNotify(recentInput);
            inputText.caretPosition = inputText.text.Length;
        }

        public string GetParameter(int index)
        {
            string[] parameters = inputText.text.Split(' ');

            if (index < parameters.Length && !string.IsNullOrWhiteSpace(parameters[index]))
                return parameters[index];

            return string.Empty;
        }

        private enum HistoryInput
        {
            None, Up, Down
        }
    }
}