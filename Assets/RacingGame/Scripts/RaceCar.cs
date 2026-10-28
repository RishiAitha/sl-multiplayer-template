using Unity.Netcode;
using UnityEngine;

public class RaceCar : NetworkBehaviour
{
    [SerializeField] private Sprite[] sprites;
    [SerializeField] private float laneSpacing = 1.5f;
    [SerializeField] private float stepDistance = 0.7f;

    private readonly NetworkVariable<int> lane = new NetworkVariable<int>();
    private readonly NetworkVariable<bool> won = new NetworkVariable<bool>();
    private SpriteRenderer spriteRenderer;
    private WordBank wordBank;

    public int Progress => wordBank.Progress;

    public override void OnNetworkSpawn()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        wordBank = GetComponent<WordBank>();

        if (IsServer)
        {
            var clients = NetworkManager.Singleton.ConnectedClientsIds;
            for (int i = 0; i < clients.Count; i++)
            {
                if (clients[i] == OwnerClientId)
                {
                    lane.Value = i;
                }
            }
        }

        lane.OnValueChanged += OnLaneChanged;
        wordBank.ProgressChanged += OnProgressChanged;
        won.OnValueChanged += OnWonChanged;
        UpdateCar();
        if (won.Value) ShowResult();
    }

    public override void OnNetworkDespawn()
    {
        lane.OnValueChanged -= OnLaneChanged;
        wordBank.ProgressChanged -= OnProgressChanged;
        won.OnValueChanged -= OnWonChanged;
    }

    private void OnLaneChanged(int previous, int current) => UpdateCar();
    private void OnProgressChanged(int previous, int current)
    {
        UpdateCar();
        if (IsServer && current >= wordBank.WordCount) won.Value = true;
    }
    private void OnWonChanged(bool previous, bool current)
    {
        if (current) ShowResult();
    }

    private void UpdateCar()
    {
        spriteRenderer.sprite = sprites[lane.Value % sprites.Length];
        transform.position = new Vector3((lane.Value - 1) * laneSpacing, -3f + wordBank.Progress * stepDistance, 0f);
    }

    private void ShowResult()
    {
        FindFirstObjectByType<SessionMenu>().ShowResult(IsOwner);
    }
}
