using Unity.Netcode;
using UnityEngine;
using System;

public class WordBank : NetworkBehaviour
{
    [SerializeField] private string[] words = { "yellow", "red", "owl", "dad", "elephant" };

    private readonly NetworkVariable<int> progress = new NetworkVariable<int>();
    private WordPromptDisplay wordDisplay;

    public int Progress => progress.Value;
    public int WordCount => words.Length;
    public string[] Words => words;
    public string CurrentWord => progress.Value < WordCount ? words[progress.Value] : string.Empty;
    public event Action<int, int> ProgressChanged;

    public override void OnNetworkSpawn()
    {
        progress.OnValueChanged += OnProgressChanged;
        if (IsServer) progress.Value = 0;
        if (IsOwner)
        {
            wordDisplay = FindFirstObjectByType<WordPromptDisplay>(FindObjectsInactive.Include);
            wordDisplay.SetWord(CurrentWord);
        }
    }

    public override void OnNetworkDespawn()
    {
        progress.OnValueChanged -= OnProgressChanged;
        if (IsOwner) wordDisplay.SetWord(string.Empty);
    }

    public void SubmitSign(string sign)
    {
        SubmitSignRpc(sign.Trim());
    }

    [Rpc(SendTo.Server)]
    private void SubmitSignRpc(string sign)
    {
        if (progress.Value < WordCount && string.Equals(sign, words[progress.Value], StringComparison.OrdinalIgnoreCase))
        {
            progress.Value++;
        }
    }

    private void OnProgressChanged(int previous, int current)
    {
        ProgressChanged?.Invoke(previous, current);
        if (IsOwner) wordDisplay.SetWord(CurrentWord);
    }
}
