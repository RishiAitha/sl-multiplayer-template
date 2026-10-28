using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SessionMenu : MonoBehaviour
{
    [SerializeField] private GameObject startPanel;
    [SerializeField] private GameObject replayPanel;
    [SerializeField] private GameObject leaveButtonObject;
    [SerializeField] private TextMeshProUGUI startStatus;
    [SerializeField] private TextMeshProUGUI replayStatus;
    [SerializeField] private TextMeshProUGUI result;
    [SerializeField] private Button startButton;
    [SerializeField] private Button replayButton;
    [SerializeField] private Button startExitButton;
    [SerializeField] private Button replayExitButton;
    [SerializeField] private Button leaveButton;

    private MultiplayerManager multiplayer;
    private GameObject gameMenu;

    private void Start()
    {
        multiplayer = FindFirstObjectByType<MultiplayerManager>();
        gameMenu = FindFirstObjectByType<GameSLRSample>(FindObjectsInactive.Include).gameObject;
        multiplayer.StatusChanged += SetStatus;
        multiplayer.SessionJoined += Hide;
        SetStatus(multiplayer.Status);
        startButton.onClick.AddListener(multiplayer.StartMatchmaking);
        replayButton.onClick.AddListener(multiplayer.StartMatchmaking);
        startExitButton.onClick.AddListener(Application.Quit);
        replayExitButton.onClick.AddListener(Leave);
        leaveButton.onClick.AddListener(Leave);
        startPanel.SetActive(true);
        replayPanel.SetActive(false);
        leaveButtonObject.SetActive(false);
        gameMenu.SetActive(false);
    }

    private void OnDestroy()
    {
        multiplayer.StatusChanged -= SetStatus;
        multiplayer.SessionJoined -= Hide;
    }

    private void SetStatus(string value)
    {
        startStatus.text = value;
        replayStatus.text = value;
    }

    private void Hide()
    {
        startPanel.SetActive(false);
        replayPanel.SetActive(false);
        leaveButtonObject.SetActive(true);
        gameMenu.SetActive(true);
    }

    private void Leave()
    {
        gameMenu.SetActive(false);
        multiplayer.Leave();
        replayPanel.SetActive(false);
        leaveButtonObject.SetActive(false);
        startPanel.SetActive(true);
    }

    public void ShowResult(bool won)
    {
        gameMenu.SetActive(false);
        result.text = won ? "You win!" : "You lose!";
        leaveButtonObject.SetActive(false);
        replayPanel.SetActive(true);
    }
}
