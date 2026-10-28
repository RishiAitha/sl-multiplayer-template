using Unity.Netcode;
using UnityEngine;

public class Monster : NetworkBehaviour
{
    [SerializeField] private Sprite[] sprites;
    [SerializeField] private Transform healthFill;
    private readonly NetworkVariable<int> health = new NetworkVariable<int>();
    private readonly NetworkVariable<int> playerNumber = new NetworkVariable<int>();
    private SpriteRenderer spriteRenderer;
    private WordBank wordBank;

    public int Progress => wordBank.Progress;
    public int Health => health.Value;

    public override void OnNetworkSpawn()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        wordBank = GetComponent<WordBank>();

        if (IsServer)
        {
            health.Value = 100;
            var clients = NetworkManager.Singleton.ConnectedClientsIds;
            for (int i = 0; i < clients.Count; i++)
            {
                if (clients[i] == OwnerClientId)
                {
                    playerNumber.Value = i;
                }
            }
        }

        playerNumber.OnValueChanged += OnPlayerNumberChanged;
        health.OnValueChanged += OnHealthChanged;
        wordBank.ProgressChanged += OnProgressChanged;
        spriteRenderer.sprite = sprites[playerNumber.Value % sprites.Length];
        OnHealthChanged(health.Value, health.Value);
        transform.position = IsOwner ? new Vector3(-1.5f, -2f, 0f) : new Vector3(1.5f, 0f, 0f);
    }

    public override void OnNetworkDespawn()
    {
        playerNumber.OnValueChanged -= OnPlayerNumberChanged;
        health.OnValueChanged -= OnHealthChanged;
        wordBank.ProgressChanged -= OnProgressChanged;
    }

    private void OnPlayerNumberChanged(int previous, int current)
    {
        spriteRenderer.sprite = sprites[current % sprites.Length];
    }

    private void OnHealthChanged(int previous, int current)
    {
        healthFill.localScale = new Vector3(Mathf.Clamp01(current / 100f), 1f, 1f);
    }

    private void OnProgressChanged(int previous, int current)
    {
        if (IsServer)
        {
            foreach (var monster in FindObjectsByType<Monster>(FindObjectsSortMode.None))
            {
                if (monster != this)
                {
                    monster.health.Value -= 100 / wordBank.WordCount;
                    if (current >= wordBank.WordCount)
                    {
                        monster.health.Value = 0;
                    }
                }
            }
        }

        if (current >= wordBank.WordCount)
        {
            GameOver();
        }
    }

    private void GameOver()
    {
        FindFirstObjectByType<SessionMenu>().GameOver(IsOwner);
    }
}
