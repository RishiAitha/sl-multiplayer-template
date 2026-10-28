using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class WordPromptDisplay : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private Image background;
    private void Awake() => SetWord(label.text);

    public void SetWord(string value)
    {
        label.text = value;
        bool show = !string.IsNullOrEmpty(value);
        background.enabled = show;
        label.enabled = show;
    }
}
